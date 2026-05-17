// -----------------------------------------------------------------------
// <copyright file="QdrantVectorStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Compendium.Abstractions.VectorStore;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant.Internal;
using Compendium.Adapters.Qdrant.Options;
using Compendium.Adapters.Qdrant.Security;
using Compendium.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Qdrant;

/// <summary>
/// Qdrant-backed <see cref="IVectorStore"/>. Speaks the Qdrant REST API directly
/// (no vendor SDK) for both self-hosted and Qdrant Cloud deployments.
/// </summary>
/// <remarks>
/// <para>Collection layout:</para>
/// <list type="bullet">
///   <item>One Qdrant collection per logical collection (with an optional prefix from <see cref="QdrantOptions.CollectionPrefix"/>).</item>
///   <item>HNSW index with caller-controlled <c>m</c> / <c>ef_construct</c>.</item>
///   <item>Per-point payload field <c>tenant_id</c> — every search filters on it.</item>
/// </list>
/// <para>Tenancy: every <see cref="VectorRecord"/>'s tenant id is validated against
/// <see cref="TenantIdentifier.IsValid"/> before bind. Cross-tenant reads are
/// impossible without explicitly passing a tenant id via
/// <see cref="VectorFilter.ForTenant"/>.</para>
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by DI as IVectorStore.")]
public sealed class QdrantVectorStore : IVectorStore
{
    private readonly QdrantOptions _options;
    private readonly QdrantHttpClient _client;
    private readonly ILogger<QdrantVectorStore> _logger;

    /// <summary>
    /// Creates a new <see cref="QdrantVectorStore"/>.
    /// </summary>
    public QdrantVectorStore(
        HttpClient httpClient,
        IOptions<QdrantOptions> options,
        ILogger<QdrantVectorStore> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value ?? throw new ArgumentException("Options.Value is null.", nameof(options));
        _client = new QdrantHttpClient(httpClient, options);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result> EnsureCollectionAsync(
        string collection,
        int dimension,
        DistanceMetric metric,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Qdrant.InvalidCollection", "Collection name cannot be null or whitespace.");
        }

        if (dimension <= 0)
        {
            return Error.Validation("Qdrant.InvalidDimension", $"Dimension must be positive, got {dimension}.");
        }

        var resolved = CollectionNaming.Resolve(_options, collection);
        if (!CollectionNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Qdrant.InvalidCollection",
                $"Collection name '{resolved}' contains characters outside [a-zA-Z0-9_-] or exceeds {CollectionNaming.MaxLength} chars.");
        }

        if (!Enum.IsDefined(metric))
        {
            return Error.Validation(
                "Qdrant.InvalidDistanceMetric",
                $"Distance metric '{metric}' is not a defined DistanceMetric value.");
        }

        var distance = DistanceMetricMap.Label(metric);

        var getResult = await _client
            .GetOptionalAsync<GetCollectionResponse>($"/collections/{Uri.EscapeDataString(resolved)}", cancellationToken)
            .ConfigureAwait(false);

        if (getResult.IsFailure)
        {
            return Result.Failure(getResult.Error);
        }

        if (getResult.Value is not null)
        {
            // Collection exists — validate compatibility.
            var existingVectors = getResult.Value.Result?.Config?.Params?.Vectors;
            if (existingVectors is not null)
            {
                if (existingVectors.Size != dimension)
                {
                    return VectorStoreErrors.DimensionMismatch(dimension, existingVectors.Size);
                }

                if (!string.Equals(existingVectors.Distance, distance, StringComparison.Ordinal))
                {
                    return Error.Conflict(
                        "Qdrant.MetricMismatch",
                        $"Collection '{resolved}' already exists with distance '{existingVectors.Distance}', cannot be re-created with '{distance}'.");
                }
            }

            _logger.LogDebug("Qdrant collection '{Collection}' already exists; skipping create.", resolved);
            return Result.Success();
        }

        var body = new CreateCollectionRequest
        {
            Vectors = new VectorParams { Size = dimension, Distance = distance },
            HnswConfig = new HnswConfig
            {
                M = _options.HnswM,
                EfConstruct = _options.HnswEfConstruct,
            },
        };

        var createResult = await _client
            .SendJsonAsync<CreateCollectionRequest, QdrantAck>(
                HttpMethod.Put,
                $"/collections/{Uri.EscapeDataString(resolved)}",
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (createResult.IsFailure)
        {
            // 409 Conflict on a concurrent create is benign — treat as success.
            if (string.Equals(createResult.Error.Code, "Qdrant.Conflict", StringComparison.Ordinal))
            {
                _logger.LogDebug(
                    "Qdrant collection '{Collection}' raced with another writer; treating as ensured.",
                    resolved);
                return Result.Success();
            }

            return Result.Failure(createResult.Error);
        }

        _logger.LogInformation(
            "Qdrant collection '{Collection}' created (dim={Dimension}, distance={Distance}, m={M}, ef_construct={Ef}).",
            resolved,
            dimension,
            distance,
            _options.HnswM,
            _options.HnswEfConstruct);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> UpsertAsync(
        string collection,
        IReadOnlyList<VectorRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Qdrant.InvalidCollection", "Collection name cannot be null or whitespace.");
        }

        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return Result.Success();
        }

        var resolved = CollectionNaming.Resolve(_options, collection);
        if (!CollectionNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Qdrant.InvalidCollection",
                $"Collection name '{resolved}' contains characters outside [a-zA-Z0-9_-].");
        }

        var points = new List<UpsertPoint>(records.Count);
        foreach (var record in records)
        {
            if (record is null)
            {
                return Error.Validation("Qdrant.InvalidRecord", "Records cannot contain null entries.");
            }

            if (string.IsNullOrWhiteSpace(record.Id))
            {
                return Error.Validation("Qdrant.InvalidRecordId", "VectorRecord.Id cannot be null or whitespace.");
            }

            if (record.TenantId is not null && !TenantIdentifier.IsValid(record.TenantId))
            {
                return Error.Validation(
                    "Qdrant.InvalidTenantId",
                    $"Record '{record.Id}' has invalid tenant id '{record.TenantId}'.");
            }

            var payload = MetadataSerializer.ToPayload(record.Metadata);
            if (record.TenantId is not null)
            {
                payload[VectorFilterTranslator.TenantPayloadKey] = record.TenantId;
            }

            points.Add(new UpsertPoint
            {
                Id = record.Id,
                Vector = record.Embedding.ToArray(),
                Payload = payload,
            });
        }

        var url = $"/collections/{Uri.EscapeDataString(resolved)}/points?wait={(_options.WaitForUpsert ? "true" : "false")}";
        var result = await _client
            .SendJsonAsync<UpsertPointsRequest, QdrantAck>(
                HttpMethod.Put,
                url,
                new UpsertPointsRequest { Points = points },
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (string.Equals(result.Error.Code, "Qdrant.NotFound", StringComparison.Ordinal))
            {
                return VectorStoreErrors.CollectionNotFound(collection);
            }

            _logger.LogError("Qdrant Upsert failed for '{Collection}': {Error}", resolved, result.Error.Message);
            return Result.Failure(result.Error);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        string collection,
        IReadOnlyList<string> ids,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Qdrant.InvalidCollection", "Collection name cannot be null or whitespace.");
        }

        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return Result.Success();
        }

        if (tenantId is not null && !TenantIdentifier.IsValid(tenantId))
        {
            return Error.Validation(
                "Qdrant.InvalidTenantId",
                $"Tenant id '{tenantId}' is not a valid identifier.");
        }

        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Error.Validation("Qdrant.InvalidRecordId", "Id list cannot contain null or whitespace entries.");
            }
        }

        var resolved = CollectionNaming.Resolve(_options, collection);
        if (!CollectionNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Qdrant.InvalidCollection",
                $"Collection name '{resolved}' contains characters outside [a-zA-Z0-9_-].");
        }

        DeletePointsRequest body;
        if (tenantId is null)
        {
            // No tenant constraint — delete by ids directly.
            body = new DeletePointsRequest
            {
                Points = new DeletePointsSelector { Points = new List<string>(ids) },
            };
        }
        else
        {
            // Tenant-scoped delete: emit a filter requiring tenant_id = X AND id ∈ ids.
            body = new DeletePointsRequest
            {
                Filter = new Filter
                {
                    Must = new List<FilterCondition>
                    {
                        new()
                        {
                            Key = VectorFilterTranslator.TenantPayloadKey,
                            Match = new MatchValue { Value = tenantId },
                        },
                        new()
                        {
                            Key = "id",
                            Match = new MatchValue { Any = new List<object?>(ids.Select(static i => (object?)i)) },
                        },
                    },
                },
            };
        }

        var url = $"/collections/{Uri.EscapeDataString(resolved)}/points/delete?wait={(_options.WaitForUpsert ? "true" : "false")}";
        var result = await _client
            .SendJsonAsync<DeletePointsRequest, QdrantAck>(
                HttpMethod.Post,
                url,
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (string.Equals(result.Error.Code, "Qdrant.NotFound", StringComparison.Ordinal))
            {
                return VectorStoreErrors.CollectionNotFound(collection);
            }

            _logger.LogError("Qdrant Delete failed for '{Collection}': {Error}", resolved, result.Error.Message);
            return Result.Failure(result.Error);
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<VectorMatch>>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> query,
        int topK,
        VectorFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return Error.Validation("Qdrant.InvalidCollection", "Collection name cannot be null or whitespace.");
        }

        if (topK <= 0)
        {
            return Error.Validation("Qdrant.InvalidTopK", $"topK must be positive, got {topK}.");
        }

        if (query.Length == 0)
        {
            return Error.Validation("Qdrant.EmptyQueryVector", "Query embedding cannot be empty.");
        }

        var resolved = CollectionNaming.Resolve(_options, collection);
        if (!CollectionNaming.IsValid(resolved))
        {
            return Error.Validation(
                "Qdrant.InvalidCollection",
                $"Collection name '{resolved}' contains characters outside [a-zA-Z0-9_-].");
        }

        var translated = VectorFilterTranslator.Build(filter, tenantOverride: null);
        if (translated.IsFailure)
        {
            return Result.Failure<IReadOnlyList<VectorMatch>>(VectorStoreErrors.InvalidFilter(translated.Error.Message));
        }

        var body = new SearchPointsRequest
        {
            Vector = query.ToArray(),
            Limit = topK,
            WithPayload = true,
            Filter = translated.Value,
        };

        var result = await _client
            .SendJsonAsync<SearchPointsRequest, SearchPointsResponse>(
                HttpMethod.Post,
                $"/collections/{Uri.EscapeDataString(resolved)}/points/search",
                body,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            if (string.Equals(result.Error.Code, "Qdrant.NotFound", StringComparison.Ordinal))
            {
                return VectorStoreErrors.CollectionNotFound(collection);
            }

            _logger.LogError("Qdrant Search failed for '{Collection}': {Error}", resolved, result.Error.Message);
            return Result.Failure<IReadOnlyList<VectorMatch>>(result.Error);
        }

        var hits = result.Value.Result ?? new List<SearchHit>();
        var matches = new List<VectorMatch>(hits.Count);
        foreach (var hit in hits)
        {
            string? tenant = null;
            if (hit.Payload is not null
                && hit.Payload.TryGetValue(VectorFilterTranslator.TenantPayloadKey, out var rawTenant)
                && rawTenant is not null)
            {
                tenant = rawTenant switch
                {
                    string s => s,
                    System.Text.Json.JsonElement el when el.ValueKind == System.Text.Json.JsonValueKind.String => el.GetString(),
                    _ => rawTenant.ToString(),
                };
            }

            matches.Add(new VectorMatch(
                hit.Id,
                hit.Score,
                MetadataSerializer.FromPayload(hit.Payload),
                tenant));
        }

        _ = CultureInfo.InvariantCulture; // keep using; avoid unused-using warning
        return Result.Success<IReadOnlyList<VectorMatch>>(matches);
    }
}
