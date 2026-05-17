// -----------------------------------------------------------------------
// <copyright file="MetadataSerializer.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;

namespace Compendium.Adapters.Qdrant.Internal;

/// <summary>
/// Round-trips a <see cref="IReadOnlyDictionary{TKey, TValue}"/> of metadata through
/// Qdrant's JSON <c>payload</c> field.
/// </summary>
internal static class MetadataSerializer
{
    /// <summary>
    /// Materialises a payload dictionary suitable for the Qdrant <c>payload</c> field.
    /// Returns an empty dictionary when <paramref name="metadata"/> is null or empty.
    /// Values that aren't natively JSON-serialisable as primitives go through
    /// <see cref="object.ToString"/>; this matches pgvector's relaxed posture.
    /// </summary>
    public static Dictionary<string, object?> ToPayload(IReadOnlyDictionary<string, object>? metadata)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (metadata is null || metadata.Count == 0)
        {
            return result;
        }

        foreach (var kvp in metadata)
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    /// <summary>
    /// Converts a Qdrant payload back into a metadata dictionary. <c>JsonElement</c>
    /// values (which is what System.Text.Json deserialises <c>object</c> to) are
    /// unwrapped into native .NET types.
    /// </summary>
    public static IReadOnlyDictionary<string, object> FromPayload(IReadOnlyDictionary<string, object?>? payload)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        if (payload is null || payload.Count == 0)
        {
            return result;
        }

        foreach (var kvp in payload)
        {
            // Skip the auto-injected tenant key so callers see the metadata they originally
            // upserted, not the adapter's bookkeeping.
            if (string.Equals(kvp.Key, VectorFilterTranslator.TenantPayloadKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (kvp.Value is null)
            {
                continue;
            }

            result[kvp.Key] = Unwrap(kvp.Value);
        }

        return result;
    }

    private static object Unwrap(object value) => value switch
    {
        JsonElement element => UnwrapElement(element),
        _ => value,
    };

    private static object UnwrapElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.TryGetInt64(out var l)
            ? l
            : (element.TryGetDouble(out var d) ? d : (object)element.GetDecimal()),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Array => element.EnumerateArray().Select(UnwrapElement).ToArray(),
        JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => UnwrapElement(p.Value)),
        _ => element.ToString(),
    };
}
