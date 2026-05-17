// -----------------------------------------------------------------------
// <copyright file="QdrantOptions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace Compendium.Adapters.Qdrant.Options;

/// <summary>
/// Index type used by Qdrant collections for approximate nearest neighbour search.
/// </summary>
public enum QdrantIndexType
{
    /// <summary>
    /// Hierarchical Navigable Small World — Qdrant's only ANN index (default).
    /// </summary>
    Hnsw = 0,
}

/// <summary>
/// Configuration for <see cref="QdrantVectorStore"/>.
/// Bound from <c>Compendium:Adapters:Qdrant</c> by default.
/// </summary>
public sealed class QdrantOptions
{
    /// <summary>
    /// Configuration section name used by <c>IConfiguration.GetSection(...)</c>.
    /// </summary>
    public const string SectionName = "Compendium:Adapters:Qdrant";

    /// <summary>
    /// Qdrant base URL. Default: <c>http://localhost:6333</c> (self-hosted dev).
    /// For Qdrant Cloud, use the cluster URL (e.g. <c>https://xxx.qdrant.io:6333</c>).
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:6333";

    /// <summary>
    /// API key for the <c>api-key</c> header. Optional — leave blank for self-hosted
    /// deployments without API-key auth. Required by Qdrant Cloud.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional prefix applied to every collection name. Default empty.
    /// Useful when one Qdrant cluster is shared across environments
    /// (e.g. <c>dev_</c>, <c>staging_</c>).
    /// </summary>
    public string CollectionPrefix { get; set; } = string.Empty;

    /// <summary>
    /// ANN index built when a collection is created. Default <see cref="QdrantIndexType.Hnsw"/>.
    /// </summary>
    public QdrantIndexType DefaultIndex { get; set; } = QdrantIndexType.Hnsw;

    /// <summary>
    /// HNSW <c>m</c> parameter (graph degree). Default 16.
    /// </summary>
    [Range(2, 1000)]
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW <c>ef_construct</c> parameter (build-time candidate-list size). Default 128.
    /// </summary>
    [Range(4, 1000)]
    public int HnswEfConstruct { get; set; } = 128;

    /// <summary>
    /// Whether to wait for Qdrant to confirm writes (<c>wait=true</c>). Default true.
    /// </summary>
    public bool WaitForUpsert { get; set; } = true;

    /// <summary>
    /// Per-request timeout. Default 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
