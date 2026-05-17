// -----------------------------------------------------------------------
// <copyright file="QdrantFixture.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Compendium.Adapters.Qdrant.IntegrationTests.Fixtures;

/// <summary>
/// Shared xUnit fixture that starts a Qdrant container (<c>qdrant/qdrant:latest</c>)
/// and exposes the mapped HTTP URL.
/// </summary>
public sealed class QdrantFixture : IAsyncLifetime
{
    private IContainer? _container;

    /// <summary>Mapped HTTP base URL of the running Qdrant.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public bool IsAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        if (!DockerDetection.IsDockerAvailable)
        {
            IsAvailable = false;
            return;
        }

#pragma warning disable CS0618 // Use modern ContainerBuilder(image) ctor when Testcontainers 5.x ships
        _container = new ContainerBuilder()
            .WithImage("qdrant/qdrant:latest")
            .WithPortBinding(6333, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPath("/readyz").ForPort(6333)))
            .WithCleanUp(true)
            .Build();
#pragma warning restore CS0618

        await _container.StartAsync();
        BaseUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(6333)}";
        IsAvailable = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
