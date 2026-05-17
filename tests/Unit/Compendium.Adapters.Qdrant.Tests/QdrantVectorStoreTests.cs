// -----------------------------------------------------------------------
// <copyright file="QdrantVectorStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Text.Json;
using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Compendium.Adapters.Qdrant.Tests;

/// <summary>
/// Behavioural coverage for <see cref="QdrantVectorStore"/> driven by
/// <see cref="MockHttpMessageHandler"/>. No real Qdrant is ever touched —
/// every test asserts both the request shape we send and how we map the
/// canned response back into <c>Result</c> values.
/// </summary>
public class QdrantVectorStoreTests
{
    private const string BaseUrl = "http://qdrant.test:6333";

    private static (QdrantVectorStore Store, MockHttpMessageHandler Mock) CreateStore(
        QdrantOptions? options = null,
        ILogger<QdrantVectorStore>? logger = null)
    {
        var mock = new MockHttpMessageHandler();
        var http = mock.ToHttpClient();
        http.BaseAddress = new Uri(BaseUrl);

        var opts = Microsoft.Extensions.Options.Options.Create(options ?? new QdrantOptions { BaseUrl = BaseUrl });
        var store = new QdrantVectorStore(http, opts, logger ?? NullLogger<QdrantVectorStore>.Instance);
        return (store, mock);
    }

    // ─── Constructor ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        // Arrange / Act
        var act = () => new QdrantVectorStore(
            null!,
            Microsoft.Extensions.Options.Options.Create(new QdrantOptions { BaseUrl = BaseUrl }),
            NullLogger<QdrantVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        // Arrange / Act
        var act = () => new QdrantVectorStore(new HttpClient(), null!, NullLogger<QdrantVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        // Arrange / Act
        var act = () => new QdrantVectorStore(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new QdrantOptions { BaseUrl = BaseUrl }),
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyBaseUrl_Throws()
    {
        // Arrange
        var opts = Microsoft.Extensions.Options.Options.Create(new QdrantOptions { BaseUrl = string.Empty });

        // Act
        var act = () => new QdrantVectorStore(new HttpClient(), opts, NullLogger<QdrantVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullOptionsValue_Throws()
    {
        // Arrange
        var opts = Substitute.For<IOptions<QdrantOptions>>();
        opts.Value.Returns((QdrantOptions)null!);

        // Act
        var act = () => new QdrantVectorStore(new HttpClient(), opts, NullLogger<QdrantVectorStore>.Instance);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithApiKey_RegistersHeader()
    {
        // Arrange
        var http = new HttpClient();
        var opts = Microsoft.Extensions.Options.Options.Create(new QdrantOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = "secret-key",
        });

        // Act
        _ = new QdrantVectorStore(http, opts, NullLogger<QdrantVectorStore>.Instance);

        // Assert
        http.DefaultRequestHeaders.GetValues("api-key").Should().ContainSingle().Which.Should().Be("secret-key");
    }

    // ─── EnsureCollectionAsync ────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnsureCollectionAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.EnsureCollectionAsync(collection!, 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidCollection");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task EnsureCollectionAsync_NonPositiveDimension_ReturnsValidation(int dim)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.EnsureCollectionAsync("documents", dim, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidDimension");
    }

    [Fact]
    public async Task EnsureCollectionAsync_BadCollectionCharacters_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.EnsureCollectionAsync("with space", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidCollection");
    }

    [Fact]
    public async Task EnsureCollectionAsync_GetReturnsNotFound_CallsCreate()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        string? capturedBody = null;
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents")
            .With(req =>
            {
                capturedBody = req.Content!.ReadAsStringAsync().Result;
                return true;
            })
            .Respond("application/json", """{"status":"ok"}""");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("vectors").GetProperty("size").GetInt32().Should().Be(3);
        doc.RootElement.GetProperty("vectors").GetProperty("distance").GetString().Should().Be("Cosine");
        doc.RootElement.GetProperty("hnsw_config").GetProperty("m").GetInt32().Should().Be(16);
        doc.RootElement.GetProperty("hnsw_config").GetProperty("ef_construct").GetInt32().Should().Be(128);
    }

    [Theory]
    [InlineData(DistanceMetric.L2, "Euclid")]
    [InlineData(DistanceMetric.InnerProduct, "Dot")]
    [InlineData(DistanceMetric.Cosine, "Cosine")]
    public async Task EnsureCollectionAsync_MapsDistanceMetric(DistanceMetric metric, string expectedLabel)
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        string? capturedBody = null;
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"status":"ok"}""");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, metric, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().Contain($"\"distance\":\"{expectedLabel}\"");
    }

    [Fact]
    public async Task EnsureCollectionAsync_AlreadyExistsCompatible_SkipsCreate()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond("application/json", """
            {
              "result": {
                "config": {
                  "params": {
                    "vectors": { "size": 3, "distance": "Cosine" }
                  }
                }
              },
              "status": "ok"
            }
            """);
        var putHits = 0;
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents")
            .Respond(req => { putHits++; return new HttpResponseMessage(HttpStatusCode.OK); });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        putHits.Should().Be(0);
    }

    [Fact]
    public async Task EnsureCollectionAsync_ExistsWithDifferentDimension_ReturnsDimensionMismatch()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond("application/json", """
            {
              "result": {
                "config": {
                  "params": {
                    "vectors": { "size": 5, "distance": "Cosine" }
                  }
                }
              },
              "status": "ok"
            }
            """);

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.DimensionMismatch");
    }

    [Fact]
    public async Task EnsureCollectionAsync_ExistsWithDifferentMetric_ReturnsConflict()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond("application/json", """
            {
              "result": {
                "config": {
                  "params": {
                    "vectors": { "size": 3, "distance": "Euclid" }
                  }
                }
              }
            }
            """);

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.MetricMismatch");
    }

    [Fact]
    public async Task EnsureCollectionAsync_CreateReturns409_TreatsAsSuccess()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.Conflict, "application/json", """{"status":"conflict"}""");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureCollectionAsync_GetFails500_PropagatesError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "boom");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.ServerError");
    }

    [Fact]
    public async Task EnsureCollectionAsync_CreateFails500_PropagatesError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.InternalServerError, "text/plain", "boom");

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.ServerError");
    }

    [Fact]
    public async Task EnsureCollectionAsync_CancellationPropagates()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Get, $"{BaseUrl}/collections/documents")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── UpsertAsync ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpsertAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync(collection!, records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidCollection");
    }

    [Fact]
    public async Task UpsertAsync_NullRecords_Throws()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        Func<Task> act = () => store.UpsertAsync("documents", null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpsertAsync_EmptyList_ReturnsSuccessWithoutHittingNetwork()
    {
        // Arrange
        var (store, mock) = CreateStore();

        // Act
        var result = await store.UpsertAsync("documents", new List<VectorRecord>(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        mock.GetMatchCount(mock.Fallback).Should().Be(0);
    }

    [Fact]
    public async Task UpsertAsync_BlankId_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord>
        {
            new("  ", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidRecordId");
    }

    [Fact]
    public async Task UpsertAsync_InvalidTenant_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>(), "bad tenant"),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidTenantId");
    }

    [Fact]
    public async Task UpsertAsync_AutoInjectsTenantPayload()
    {
        // Arrange
        var (store, mock) = CreateStore();
        string? capturedBody = null;
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents/points*")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"status":"ok"}""");

        var records = new List<VectorRecord>
        {
            new(
                "id1",
                new float[] { 1, 2, 3 },
                new Dictionary<string, object> { ["title"] = "hello" },
                "tenant-1"),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var point = doc.RootElement.GetProperty("points")[0];
        point.GetProperty("id").GetString().Should().Be("id1");
        point.GetProperty("payload").GetProperty("title").GetString().Should().Be("hello");
        point.GetProperty("payload").GetProperty("tenant_id").GetString().Should().Be("tenant-1");
    }

    [Fact]
    public async Task UpsertAsync_WaitForUpsertFalse_PassesWaitFalseQueryString()
    {
        // Arrange
        var (store, mock) = CreateStore(new QdrantOptions { BaseUrl = BaseUrl, WaitForUpsert = false });
        var capturedUri = string.Empty;
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents/points*")
            .With(req => { capturedUri = req.RequestUri!.ToString(); return true; })
            .Respond("application/json", """{"status":"ok"}""");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        capturedUri.Should().Contain("wait=false");
    }

    [Fact]
    public async Task UpsertAsync_CollectionNotFound_MapsToVectorStoreError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Put, $"{BaseUrl}/collections/documents/points*")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    [Fact]
    public async Task UpsertAsync_NullRecordEntry_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var records = new List<VectorRecord> { null! };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidRecord");
    }

    // ─── DeleteAsync ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync(collection!, new List<string> { "id1" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidCollection");
    }

    [Fact]
    public async Task DeleteAsync_NullIds_Throws()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        Func<Task> act = () => store.DeleteAsync("documents", null!, null, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeleteAsync_EmptyIds_ReturnsSuccessWithoutHittingNetwork()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync("documents", new List<string>(), null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_InvalidTenant_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync(
            "documents",
            new List<string> { "id1" },
            tenantId: "bad tenant",
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidTenantId");
    }

    [Fact]
    public async Task DeleteAsync_BlankIdEntry_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.DeleteAsync(
            "documents",
            new List<string> { "ok", "  " },
            tenantId: null,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidRecordId");
    }

    [Fact]
    public async Task DeleteAsync_NoTenant_SendsPointsSelector()
    {
        // Arrange
        var (store, mock) = CreateStore();
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/delete*")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"status":"ok"}""");

        // Act
        var result = await store.DeleteAsync("documents", new List<string> { "a", "b" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.GetProperty("points").GetProperty("points").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_WithTenant_SendsFilteredDelete()
    {
        // Arrange
        var (store, mock) = CreateStore();
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/delete*")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"status":"ok"}""");

        // Act
        var result = await store.DeleteAsync(
            "documents",
            new List<string> { "a" },
            tenantId: "tenant-1",
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var must = doc.RootElement.GetProperty("filter").GetProperty("must");
        must.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_MapsToVectorStoreError()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/delete*")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        // Act
        var result = await store.DeleteAsync("documents", new List<string> { "a" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    // ─── SearchAsync ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_BadCollection_ReturnsValidation(string? collection)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.SearchAsync(collection!, new float[] { 1, 2, 3 }, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidCollection");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_NonPositiveTopK_ReturnsValidation(int topK)
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, topK, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidTopK");
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();

        // Act
        var result = await store.SearchAsync("documents", ReadOnlyMemory<float>.Empty, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.EmptyQueryVector");
    }

    [Fact]
    public async Task SearchAsync_InvalidFilter_ReturnsValidation()
    {
        // Arrange
        var (store, _) = CreateStore();
        var filter = VectorFilter.Eq("category", "support").ForTenant("bad tenant");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.InvalidFilter");
    }

    [Fact]
    public async Task SearchAsync_HappyPath_MapsHits()
    {
        // Arrange
        var (store, mock) = CreateStore();
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/search")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """
            {
              "result": [
                { "id": "a", "score": 0.9, "payload": { "title": "alpha", "tenant_id": "tenant-1" } },
                { "id": 42,  "score": 0.7, "payload": { "title": "forty-two" } }
              ],
              "status": "ok"
            }
            """);
        var filter = VectorFilter.Eq("category", "support").ForTenant("tenant-1");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 0.9f, 0.1f, 0f }, topK: 2, filter, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value![0].Id.Should().Be("a");
        result.Value[0].Score.Should().BeApproximately(0.9f, 1e-6f);
        result.Value[0].TenantId.Should().Be("tenant-1");
        result.Value[0].Metadata.Should().ContainKey("title");
        result.Value[0].Metadata.Should().NotContainKey("tenant_id");
        result.Value[1].Id.Should().Be("42");
        capturedBody.Should().Contain("\"limit\":2");
        capturedBody.Should().Contain("\"with_payload\":true");
        capturedBody.Should().Contain("\"tenant_id\"");
    }

    [Fact]
    public async Task SearchAsync_NoTenantFilter_EmitsMustNotOnSentinel()
    {
        // Arrange
        var (store, mock) = CreateStore();
        string? capturedBody = null;
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/search")
            .With(req => { capturedBody = req.Content!.ReadAsStringAsync().Result; return true; })
            .Respond("application/json", """{"result":[],"status":"ok"}""");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("must_not");
        capturedBody.Should().Contain("tenant_id");
    }

    [Fact]
    public async Task SearchAsync_404_MapsToCollectionNotFound()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/search")
            .Respond(HttpStatusCode.NotFound, "application/json", "{}");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("VectorStore.CollectionNotFound");
    }

    [Fact]
    public async Task SearchAsync_429_MapsToThrottled()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/search")
            .Respond((HttpStatusCode)429, "text/plain", "rate limited");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Throttled");
    }

    [Fact]
    public async Task SearchAsync_401_MapsToUnauthorized()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/search")
            .Respond(HttpStatusCode.Unauthorized, "text/plain", "denied");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Unauthorized");
    }

    [Fact]
    public async Task SearchAsync_EmptyHits_ReturnsEmptyList()
    {
        // Arrange
        var (store, mock) = CreateStore();
        mock.When(HttpMethod.Post, $"{BaseUrl}/collections/documents/points/search")
            .Respond("application/json", """{"status":"ok"}""");

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, filter: null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
