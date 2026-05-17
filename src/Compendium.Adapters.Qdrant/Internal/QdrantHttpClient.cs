// -----------------------------------------------------------------------
// <copyright file="QdrantHttpClient.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Compendium.Adapters.Qdrant.Options;
using Compendium.Core.Results;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Qdrant.Internal;

/// <summary>
/// Typed HTTP client over the Qdrant REST API. Owns request shaping (auth header,
/// JSON content), serialisation, and translation of non-success status codes into
/// <see cref="Result"/> failures.
/// </summary>
internal sealed class QdrantHttpClient
{
    private readonly HttpClient _http;

    public QdrantHttpClient(HttpClient http, IOptions<QdrantOptions> options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        var opts = options.Value ?? throw new ArgumentException("Options.Value is null.", nameof(options));

        if (string.IsNullOrWhiteSpace(opts.BaseUrl))
        {
            throw new ArgumentException("QdrantOptions.BaseUrl must be configured.", nameof(options));
        }

        _http = http;

        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(opts.BaseUrl, UriKind.Absolute);
        }

        if (!string.IsNullOrEmpty(opts.ApiKey)
            && !_http.DefaultRequestHeaders.Contains("api-key"))
        {
            _http.DefaultRequestHeaders.Add("api-key", opts.ApiKey);
        }
    }

    /// <summary>Sends a GET request and returns either the parsed body, or null when the server returned 404.</summary>
    public async Task<Result<TResponse?>> GetOptionalAsync<TResponse>(string requestUri, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result.Success<TResponse?>(null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return await MapErrorAsync<TResponse?>(response, "Get", cancellationToken).ConfigureAwait(false);
            }

            var body = await response.Content
                .ReadFromJsonAsync<TResponse>(QdrantJson.Options, cancellationToken)
                .ConfigureAwait(false);
            return Result.Success(body);
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("Qdrant.Network", $"Qdrant request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Failure("Qdrant.Timeout", $"Qdrant request timed out: {ex.Message}");
        }
    }

    /// <summary>Sends a JSON body with <see cref="HttpMethod"/>.</summary>
    public async Task<Result<TResponse>> SendJsonAsync<TRequest, TResponse>(
        HttpMethod method,
        string requestUri,
        TRequest body,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(body, mediaType: new MediaTypeHeaderValue("application/json"), options: QdrantJson.Options),
        };

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await MapErrorAsync<TResponse>(response, method.Method, cancellationToken).ConfigureAwait(false);
            }

            var parsed = await response.Content
                .ReadFromJsonAsync<TResponse>(QdrantJson.Options, cancellationToken)
                .ConfigureAwait(false);
            return Result.Success(parsed!);
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("Qdrant.Network", $"Qdrant request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.Failure("Qdrant.Timeout", $"Qdrant request timed out: {ex.Message}");
        }
    }

    private static async Task<Result<T>> MapErrorAsync<T>(
        HttpResponseMessage response,
        string verb,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        string? remote = null;

        try
        {
            // Best-effort: Qdrant returns its error JSON on most failure codes,
            // but plain bodies happen on proxy errors.
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            remote = raw.Length > 512 ? raw[..512] : raw;
        }
        catch
        {
            // ignore
        }

        var message = string.IsNullOrEmpty(remote)
            ? $"Qdrant {verb} returned HTTP {statusCode}."
            : $"Qdrant {verb} returned HTTP {statusCode}: {remote}";

        var code = statusCode switch
        {
            401 or 403 => "Qdrant.Unauthorized",
            404 => "Qdrant.NotFound",
            408 => "Qdrant.Timeout",
            409 => "Qdrant.Conflict",
            429 => "Qdrant.Throttled",
            >= 500 => "Qdrant.ServerError",
            _ => "Qdrant.HttpError",
        };

        return Error.Failure(code, message);
    }
}
