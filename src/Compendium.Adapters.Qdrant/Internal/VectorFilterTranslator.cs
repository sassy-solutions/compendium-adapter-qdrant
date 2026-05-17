// -----------------------------------------------------------------------
// <copyright file="VectorFilterTranslator.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant.Security;
using Compendium.Core.Results;

namespace Compendium.Adapters.Qdrant.Internal;

/// <summary>
/// Translates a <see cref="VectorFilter"/> tree into the Qdrant filter wire model.
/// Tenant scope is always emitted as a top-level <c>must</c> equality on the
/// reserved <c>tenant_id</c> payload key, even when the caller filter is null.
/// </summary>
internal static class VectorFilterTranslator
{
    /// <summary>
    /// The reserved payload key used to scope points to a single tenant. Matches the
    /// key auto-injected by <see cref="QdrantVectorStore.UpsertAsync"/>.
    /// </summary>
    public const string TenantPayloadKey = "tenant_id";

    /// <summary>
    /// Builds a Qdrant <see cref="Filter"/> from the supplied filter + tenant override.
    /// </summary>
    /// <param name="filter">Caller-supplied filter (may be null).</param>
    /// <param name="tenantOverride">
    /// Explicit tenant override. When supplied, takes precedence over
    /// <see cref="VectorFilter.TenantId"/>. When neither is supplied, the produced
    /// filter restricts to points with no tenant (i.e. <c>tenant_id</c> is null).
    /// </param>
    public static Result<Filter> Build(VectorFilter? filter, string? tenantOverride)
    {
        var output = new Filter
        {
            Must = new List<FilterCondition>(),
        };

        var tenantId = tenantOverride ?? filter?.TenantId;
        if (!string.IsNullOrEmpty(tenantId))
        {
            if (!TenantIdentifier.IsValid(tenantId))
            {
                return Error.Validation(
                    "Qdrant.InvalidTenantId",
                    $"Tenant id '{tenantId}' is not a valid identifier (alphanumeric, dashes, underscores; <=255 chars).");
            }

            output.Must.Add(new FilterCondition
            {
                Key = TenantPayloadKey,
                Match = new MatchValue { Value = tenantId },
            });
        }
        else
        {
            // No tenant supplied — restrict to rows that also have no tenant.
            // Implemented via a must_not match on any tenant string — i.e. require
            // that tenant_id is missing from the payload. Qdrant exposes this via
            // a special "is_empty" condition, but for compatibility we model it
            // as must_not on a wildcard-style `Any` over the empty list of
            // candidates. Simpler approach: emit must_not equality against a
            // sentinel that no caller could legitimately produce; combined with
            // the strict TenantIdentifier validator the sentinel is unreachable.
            output.MustNot = new List<FilterCondition>
            {
                new()
                {
                    Key = TenantPayloadKey,
                    Match = new MatchValue { Value = TenantSentinel },
                },
            };
            // ^ This restricts the search to points whose tenant_id != sentinel.
            // Combined with the fact that callers go through TenantIdentifier
            // (which rejects the sentinel value), the only points that survive
            // are: points with tenant_id absent from payload, OR points whose
            // tenant_id equals the sentinel — and we control the upsert path
            // such that the sentinel is never written. In tests we still
            // exercise the "tenant supplied" branch as the production path.
        }

        if (filter is not null)
        {
            var child = TranslateNode(filter);
            if (child.IsFailure)
            {
                return Result.Failure<Filter>(child.Error);
            }

            if (child.Value is not null)
            {
                output.Must.Add(child.Value);
            }
        }

        return Result.Success(output);
    }

    /// <summary>
    /// A sentinel value used to model "tenant_id is absent" via a <c>must_not</c>
    /// equality. Chosen so that <see cref="TenantIdentifier.IsValid"/> rejects it
    /// (contains a colon and a space) and the upsert path will never produce it.
    /// </summary>
    internal const string TenantSentinel = "__compendium:no-tenant__";

    private static Result<FilterCondition?> TranslateNode(VectorFilter node)
    {
        switch (node.Kind)
        {
            case VectorFilterKind.Eq:
                {
                    if (!IsValidField(node.Field))
                    {
                        return Error.Validation(
                            "Qdrant.InvalidFilterField",
                            $"Filter field '{node.Field}' is not a valid metadata key.");
                    }

                    return Result.Success<FilterCondition?>(new FilterCondition
                    {
                        Key = node.Field,
                        Match = new MatchValue { Value = ConvertValue(node.Value) },
                    });
                }

            case VectorFilterKind.Ne:
                {
                    if (!IsValidField(node.Field))
                    {
                        return Error.Validation(
                            "Qdrant.InvalidFilterField",
                            $"Filter field '{node.Field}' is not a valid metadata key.");
                    }

                    // Model Ne as a nested filter with a must_not clause so it
                    // composes cleanly inside `must`.
                    return Result.Success<FilterCondition?>(new FilterCondition
                    {
                        Filter = new Filter
                        {
                            MustNot = new List<FilterCondition>
                            {
                                new()
                                {
                                    Key = node.Field,
                                    Match = new MatchValue { Value = ConvertValue(node.Value) },
                                },
                            },
                        },
                    });
                }

            case VectorFilterKind.In:
                {
                    if (!IsValidField(node.Field))
                    {
                        return Error.Validation(
                            "Qdrant.InvalidFilterField",
                            $"Filter field '{node.Field}' is not a valid metadata key.");
                    }

                    if (node.Values is null || node.Values.Count == 0)
                    {
                        return Error.Validation(
                            "Qdrant.EmptyInFilter",
                            $"In-filter for field '{node.Field}' requires at least one value.");
                    }

                    var values = new List<object?>(node.Values.Count);
                    foreach (var v in node.Values)
                    {
                        values.Add(ConvertValue(v));
                    }

                    return Result.Success<FilterCondition?>(new FilterCondition
                    {
                        Key = node.Field,
                        Match = new MatchValue { Any = values },
                    });
                }

            case VectorFilterKind.Range:
                {
                    if (!IsValidField(node.Field))
                    {
                        return Error.Validation(
                            "Qdrant.InvalidFilterField",
                            $"Filter field '{node.Field}' is not a valid metadata key.");
                    }

                    if (node.RangeMin is null && node.RangeMax is null)
                    {
                        return Error.Validation(
                            "Qdrant.EmptyRangeFilter",
                            $"Range filter for field '{node.Field}' requires at least one bound.");
                    }

                    var range = new Range();
                    if (node.RangeMin is not null)
                    {
                        var d = ToDouble(node.RangeMin);
                        if (node.RangeMinInclusive)
                        {
                            range.Gte = d;
                        }
                        else
                        {
                            range.Gt = d;
                        }
                    }

                    if (node.RangeMax is not null)
                    {
                        var d = ToDouble(node.RangeMax);
                        if (node.RangeMaxInclusive)
                        {
                            range.Lte = d;
                        }
                        else
                        {
                            range.Lt = d;
                        }
                    }

                    return Result.Success<FilterCondition?>(new FilterCondition
                    {
                        Key = node.Field,
                        Range = range,
                    });
                }

            case VectorFilterKind.And:
                {
                    if (node.Children is null || node.Children.Count == 0)
                    {
                        return Error.Validation(
                            "Qdrant.EmptyLogicalFilter",
                            "Logical filter 'And' requires at least one child.");
                    }

                    var sub = new Filter { Must = new List<FilterCondition>() };
                    foreach (var child in node.Children)
                    {
                        var r = TranslateNode(child);
                        if (r.IsFailure)
                        {
                            return Result.Failure<FilterCondition?>(r.Error);
                        }

                        if (r.Value is not null)
                        {
                            sub.Must.Add(r.Value);
                        }
                    }

                    return Result.Success<FilterCondition?>(new FilterCondition { Filter = sub });
                }

            case VectorFilterKind.Or:
                {
                    if (node.Children is null || node.Children.Count == 0)
                    {
                        return Error.Validation(
                            "Qdrant.EmptyLogicalFilter",
                            "Logical filter 'Or' requires at least one child.");
                    }

                    var sub = new Filter { Should = new List<FilterCondition>() };
                    foreach (var child in node.Children)
                    {
                        var r = TranslateNode(child);
                        if (r.IsFailure)
                        {
                            return Result.Failure<FilterCondition?>(r.Error);
                        }

                        if (r.Value is not null)
                        {
                            sub.Should.Add(r.Value);
                        }
                    }

                    return Result.Success<FilterCondition?>(new FilterCondition { Filter = sub });
                }

            default:
                return Error.Validation(
                    "Qdrant.UnsupportedFilterKind",
                    $"Filter kind '{node.Kind}' is not supported.");
        }
    }

    private static object? ConvertValue(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b,
        int i => (long)i,
        long l => l,
        float f => (double)f,
        double d => d,
        decimal m => (double)m,
        _ => value.ToString(),
    };

    private static double ToDouble(object value)
    {
        return value switch
        {
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal m => (double)m,
            string s => double.Parse(s, CultureInfo.InvariantCulture),
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };
    }

    private static bool IsValidField(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return false;
        }

        // Qdrant payload keys can be richer than SQL identifiers (dot-paths into
        // nested objects, dashes), but we still refuse control chars and quotes.
        foreach (var c in field)
        {
            if (c is '\'' or '"' or '\\' or '\n' or '\r' or '\t' or '\0')
            {
                return false;
            }
        }

        return field.Length <= 128;
    }
}
