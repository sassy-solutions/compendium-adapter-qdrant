// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore;
using Compendium.Adapters.Qdrant.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Qdrant.DependencyInjection;

/// <summary>
/// DI registration helpers for the Qdrant adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="QdrantVectorStore"/> as <see cref="IVectorStore"/> bound to
    /// <see cref="QdrantOptions.SectionName"/>.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configuration">Source configuration; section <see cref="QdrantOptions.SectionName"/> is bound.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumQdrant(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<QdrantOptions>()
            .Bind(configuration.GetSection(QdrantOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        RegisterStore(services);
        return services;
    }

    /// <summary>
    /// Registers <see cref="QdrantVectorStore"/> as <see cref="IVectorStore"/> with an inline
    /// configuration callback.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <param name="configure">Callback to mutate <see cref="QdrantOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddCompendiumQdrant(
        this IServiceCollection services,
        Action<QdrantOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<QdrantOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        RegisterStore(services);
        return services;
    }

    private static void RegisterStore(IServiceCollection services)
    {
        services.AddHttpClient<QdrantVectorStore>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            }

            client.Timeout = options.Timeout;
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("api-key", options.ApiKey);
            }
        });

        services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<QdrantVectorStore>());
    }
}
