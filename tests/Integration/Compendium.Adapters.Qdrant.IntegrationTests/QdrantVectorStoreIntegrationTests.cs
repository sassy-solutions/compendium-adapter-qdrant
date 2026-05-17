// -----------------------------------------------------------------------
// <copyright file="QdrantVectorStoreIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant;
using Compendium.Adapters.Qdrant.IntegrationTests.Fixtures;
using Compendium.Adapters.Qdrant.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MEO = Microsoft.Extensions.Options;

namespace Compendium.Adapters.Qdrant.IntegrationTests;

[Collection("Qdrant")]
public class QdrantVectorStoreIntegrationTests(QdrantFixture fixture) : IClassFixture<QdrantFixture>
{
    private readonly QdrantFixture _fixture = fixture;

    private QdrantVectorStore CreateStore(string? prefix = null)
    {
        var options = MEO.Options.Create(new QdrantOptions
        {
            BaseUrl = _fixture.BaseUrl,
            CollectionPrefix = prefix ?? "it_",
        });
        var http = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };
        return new QdrantVectorStore(http, options, NullLogger<QdrantVectorStore>.Instance);
    }

    [RequiresDockerFact]
    public async Task EnsureCollection_CreatesCollectionAndIsIdempotent()
    {
        // Arrange
        var store = CreateStore(prefix: "t1_");

        // Act
        var first = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);
        var second = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
    }

    [RequiresDockerFact]
    public async Task EnsureCollection_DimensionMismatch_ReturnsFailure()
    {
        // Arrange
        var store = CreateStore(prefix: "t2_");
        await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Act
        var result = await store.EnsureCollectionAsync("documents", 5, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.DimensionMismatch");
    }

    [RequiresDockerFact]
    public async Task UpsertSearch_ReturnsNearestNeighbours()
    {
        // Arrange
        var store = CreateStore(prefix: "t3_");
        await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Qdrant point ids must be either an unsigned integer (decimal string is fine)
        // or a UUID. Use UUIDs for safety across the suite.
        var records = new List<VectorRecord>
        {
            new(Guid.NewGuid().ToString(), new float[] { 1f, 0f, 0f }, new Dictionary<string, object> { ["title"] = "alpha" }),
            new(Guid.NewGuid().ToString(), new float[] { 0f, 1f, 0f }, new Dictionary<string, object> { ["title"] = "beta" }),
            new(Guid.NewGuid().ToString(), new float[] { 0f, 0f, 1f }, new Dictionary<string, object> { ["title"] = "gamma" }),
            new(Guid.NewGuid().ToString(), new float[] { 1f, 1f, 0f }, new Dictionary<string, object> { ["title"] = "ne-x" }),
            new(Guid.NewGuid().ToString(), new float[] { 0.5f, 0.5f, 0.5f }, new Dictionary<string, object> { ["title"] = "origin" }),
        };

        var upsertResult = await store.UpsertAsync("documents", records, CancellationToken.None);
        upsertResult.IsSuccess.Should().BeTrue();

        // Act — query close to alpha.
        var search = await store.SearchAsync(
            "documents",
            new float[] { 0.9f, 0.1f, 0f },
            topK: 3,
            filter: null,
            CancellationToken.None);

        // Assert — top hit should be alpha (closest cosine to (1,0,0)).
        search.IsSuccess.Should().BeTrue();
        search.Value!.Should().NotBeEmpty();
        ((string)search.Value[0].Metadata["title"]).Should().Be("alpha");
    }

    [RequiresDockerFact]
    public async Task TenantIsolation_TenantBCannotSeeTenantAData()
    {
        // Arrange
        var store = CreateStore(prefix: "t4_");
        await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        var aId = Guid.NewGuid().ToString();
        var bId = Guid.NewGuid().ToString();
        await store.UpsertAsync(
            "documents",
            new List<VectorRecord>
            {
                new(aId, new float[] { 1f, 0f, 0f }, new Dictionary<string, object> { ["title"] = "a-only" }, "tenant-a"),
                new(bId, new float[] { 1f, 0f, 0f }, new Dictionary<string, object> { ["title"] = "b-only" }, "tenant-b"),
            },
            CancellationToken.None);

        // Act — search as tenant-a; we must NOT see tenant-b's record.
        var filter = VectorFilter.Eq("title", "b-only").ForTenant("tenant-a");
        var aSearch = await store.SearchAsync(
            "documents",
            new float[] { 1f, 0f, 0f },
            topK: 10,
            filter: filter,
            CancellationToken.None);

        // Assert
        aSearch.IsSuccess.Should().BeTrue();
        aSearch.Value!.Should().BeEmpty();
    }

    [RequiresDockerFact]
    public async Task DeleteAsync_IsIdempotent()
    {
        // Arrange
        var store = CreateStore(prefix: "t5_");
        await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);
        var id = Guid.NewGuid().ToString();
        await store.UpsertAsync(
            "documents",
            new List<VectorRecord>
            {
                new(id, new float[] { 1f, 0f, 0f }, new Dictionary<string, object>()),
            },
            CancellationToken.None);

        // Act — delete twice.
        var d1 = await store.DeleteAsync("documents", new List<string> { id }, null, CancellationToken.None);
        var d2 = await store.DeleteAsync("documents", new List<string> { id }, null, CancellationToken.None);

        // Assert
        d1.IsSuccess.Should().BeTrue();
        d2.IsSuccess.Should().BeTrue();
    }

    [RequiresDockerFact]
    public async Task SearchAsync_NonexistentCollection_ReturnsCollectionNotFound()
    {
        // Arrange
        var store = CreateStore(prefix: "t6_");

        // Act
        var result = await store.SearchAsync(
            "ghost_collection",
            new float[] { 1f, 0f, 0f },
            5,
            filter: null,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }
}

[CollectionDefinition("Qdrant")]
public class QdrantCollectionDefinition : ICollectionFixture<QdrantFixture>
{
    // Intentionally empty.
}
