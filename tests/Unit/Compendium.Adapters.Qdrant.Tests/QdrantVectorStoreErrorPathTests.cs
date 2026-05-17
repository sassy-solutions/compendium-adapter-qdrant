// -----------------------------------------------------------------------
// <copyright file="QdrantVectorStoreErrorPathTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.Qdrant.Tests;

/// <summary>
/// HTTP-layer error-handling for <see cref="QdrantVectorStore"/> driven by a
/// fault-injecting <see cref="HttpMessageHandler"/>. Exercises the network /
/// timeout exception branches that <see cref="RichardSzalay.MockHttp.MockHttpMessageHandler"/>
/// can't easily simulate.
/// </summary>
public class QdrantVectorStoreErrorPathTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> OnSend { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => OnSend(request, cancellationToken);
    }

    private static QdrantVectorStore CreateStore(ThrowingHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://qdrant.test:6333") };
        var opts = Microsoft.Extensions.Options.Options.Create(new QdrantOptions { BaseUrl = "http://qdrant.test:6333" });
        return new QdrantVectorStore(http, opts, NullLogger<QdrantVectorStore>.Instance);
    }

    [Fact]
    public async Task SearchAsync_HttpRequestException_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("connection refused"),
        });

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Network");
    }

    [Fact]
    public async Task SearchAsync_TaskCanceledNotByUser_MapsToTimeout()
    {
        // Arrange — simulate the HttpClient internal "client-side timeout" case where
        // TaskCanceledException fires but the user's cancellation token is not signalled.
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new TaskCanceledException("request timed out"),
        });

        // Act
        var result = await store.SearchAsync("documents", new float[] { 1, 2, 3 }, 5, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Timeout");
    }

    [Fact]
    public async Task EnsureCollectionAsync_HttpRequestExceptionOnGet_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("connection refused"),
        });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Network");
    }

    [Fact]
    public async Task EnsureCollectionAsync_TaskCanceledOnGet_MapsToTimeout()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new TaskCanceledException("timed out"),
        });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, DistanceMetric.Cosine, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Timeout");
    }

    [Fact]
    public async Task UpsertAsync_HttpRequestException_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("dial tcp: refused"),
        });
        var records = new List<VectorRecord>
        {
            new("id1", new float[] { 1, 2, 3 }, new Dictionary<string, object>()),
        };

        // Act
        var result = await store.UpsertAsync("documents", records, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Network");
    }

    [Fact]
    public async Task DeleteAsync_HttpRequestException_MapsToNetworkError()
    {
        // Arrange
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => throw new HttpRequestException("eof"),
        });

        // Act
        var result = await store.DeleteAsync("documents", new List<string> { "a" }, null, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.Network");
    }

    [Fact]
    public async Task EnsureCollectionAsync_InvalidDistanceMetricEnumValue_ReturnsValidation()
    {
        // Arrange — bypass Enum.IsDefined by casting an out-of-range int.
        var store = CreateStore(new ThrowingHandler
        {
            OnSend = (_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)),
        });

        // Act
        var result = await store.EnsureCollectionAsync("documents", 3, (DistanceMetric)999, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidDistanceMetric");
    }
}
