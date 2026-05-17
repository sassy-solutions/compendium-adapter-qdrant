// -----------------------------------------------------------------------
// <copyright file="QdrantWireModels.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Compendium.Adapters.Qdrant.Internal;

// Wire-format DTOs for the slice of the Qdrant REST API the adapter touches.
// Field naming follows Qdrant's snake_case via the shared JsonSerializerOptions
// (see QdrantJson). Kept internal — callers should never see these.

/// <summary>Body of <c>PUT /collections/{name}</c>.</summary>
internal sealed class CreateCollectionRequest
{
    public VectorParams Vectors { get; set; } = new();

    public HnswConfig? HnswConfig { get; set; }
}

/// <summary>Vector parameters within <see cref="CreateCollectionRequest"/>.</summary>
internal sealed class VectorParams
{
    public int Size { get; set; }

    public string Distance { get; set; } = "Cosine";
}

/// <summary>HNSW index tuning parameters.</summary>
internal sealed class HnswConfig
{
    public int M { get; set; }

    public int EfConstruct { get; set; }
}

/// <summary>Response of <c>GET /collections/{name}</c> (slice we use).</summary>
internal sealed class GetCollectionResponse
{
    public CollectionInfoResult? Result { get; set; }

    public string? Status { get; set; }
}

/// <summary>Inner <c>result</c> of <see cref="GetCollectionResponse"/>.</summary>
internal sealed class CollectionInfoResult
{
    public string? Status { get; set; }

    public CollectionConfig? Config { get; set; }
}

/// <summary>Collection-level config returned in collection info.</summary>
internal sealed class CollectionConfig
{
    public CollectionParams? Params { get; set; }
}

/// <summary>Collection params containing vector config.</summary>
internal sealed class CollectionParams
{
    [JsonPropertyName("vectors")]
    public VectorParams? Vectors { get; set; }
}

/// <summary>Body of <c>PUT /collections/{name}/points?wait=...</c>.</summary>
internal sealed class UpsertPointsRequest
{
    public List<UpsertPoint> Points { get; set; } = [];
}

/// <summary>A single point in <see cref="UpsertPointsRequest"/>.</summary>
internal sealed class UpsertPoint
{
    public string Id { get; set; } = string.Empty;

    public float[] Vector { get; set; } = [];

    public Dictionary<string, object?> Payload { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Body of <c>POST /collections/{name}/points/delete</c>.</summary>
internal sealed class DeletePointsRequest
{
    public DeletePointsSelector? Points { get; set; }

    public Filter? Filter { get; set; }
}

/// <summary>Selector body for id-based deletes.</summary>
internal sealed class DeletePointsSelector
{
    public List<string> Points { get; set; } = [];
}

/// <summary>Body of <c>POST /collections/{name}/points/search</c>.</summary>
internal sealed class SearchPointsRequest
{
    public float[] Vector { get; set; } = [];

    public int Limit { get; set; }

    public bool WithPayload { get; set; } = true;

    public Filter? Filter { get; set; }
}

/// <summary>Response wrapper for the Qdrant search endpoint.</summary>
internal sealed class SearchPointsResponse
{
    public List<SearchHit>? Result { get; set; }

    public string? Status { get; set; }
}

/// <summary>A single hit in the search response.</summary>
internal sealed class SearchHit
{
    [JsonConverter(typeof(QdrantIdConverter))]
    public string Id { get; set; } = string.Empty;

    public float Score { get; set; }

    public Dictionary<string, object?>? Payload { get; set; }
}

/// <summary>Generic ack envelope used by Qdrant writes / deletes.</summary>
internal sealed class QdrantAck
{
    public string? Status { get; set; }
}

// Filter primitives. Qdrant's filter language is much richer than what the
// IVectorStore abstraction surfaces today; we model only the slice we emit.

/// <summary>Qdrant filter root.</summary>
internal sealed class Filter
{
    public List<FilterCondition>? Must { get; set; }

    [JsonPropertyName("must_not")]
    public List<FilterCondition>? MustNot { get; set; }

    public List<FilterCondition>? Should { get; set; }
}

/// <summary>A single filter clause (field-condition, range, or nested boolean group).</summary>
internal sealed class FilterCondition
{
    public string? Key { get; set; }

    public MatchValue? Match { get; set; }

    public Range? Range { get; set; }

    public Filter? Filter { get; set; }
}

/// <summary>Qdrant match-value primitive.</summary>
internal sealed class MatchValue
{
    public object? Value { get; set; }

    public List<object?>? Any { get; set; }
}

/// <summary>Qdrant range primitive — all bounds are optional.</summary>
internal sealed class Range
{
    public double? Gt { get; set; }

    public double? Gte { get; set; }

    public double? Lt { get; set; }

    public double? Lte { get; set; }
}
