// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore;
using Compendium.Adapters.Qdrant.DependencyInjection;
using Compendium.Adapters.Qdrant.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Qdrant.Tests.DependencyInjection;

/// <summary>
/// DI registration semantics for the Qdrant adapter — verifies binding,
/// IVectorStore resolution, and null-argument guards.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompendiumQdrant_WithConfiguration_BindsAndRegistersIVectorStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Compendium:Adapters:Qdrant:BaseUrl"] = "https://qdrant.example.com",
                ["Compendium:Adapters:Qdrant:ApiKey"] = "k1",
            })
            .Build();

        // Act
        var actual = services.AddCompendiumQdrant(configuration);
        var sp = actual.BuildServiceProvider();

        // Assert
        actual.Should().BeSameAs(services);
        sp.GetRequiredService<IVectorStore>().Should().BeOfType<QdrantVectorStore>();
        sp.GetRequiredService<IOptions<QdrantOptions>>().Value.BaseUrl
            .Should().Be("https://qdrant.example.com");
    }

    [Fact]
    public void AddCompendiumQdrant_WithCallback_RegistersIVectorStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCompendiumQdrant(o =>
        {
            o.BaseUrl = "https://qdrant.example.com";
            o.ApiKey = "k1";
        });
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetRequiredService<IVectorStore>().Should().BeOfType<QdrantVectorStore>();
    }

    [Fact]
    public void AddCompendiumQdrant_NullServicesWithConfiguration_Throws()
    {
        // Arrange
        IServiceCollection? services = null;
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var act = () => services!.AddCompendiumQdrant(configuration);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumQdrant_NullServicesWithCallback_Throws()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddCompendiumQdrant(_ => { });

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumQdrant_NullConfiguration_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumQdrant((IConfiguration)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumQdrant_NullCallback_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumQdrant((Action<QdrantOptions>)null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
