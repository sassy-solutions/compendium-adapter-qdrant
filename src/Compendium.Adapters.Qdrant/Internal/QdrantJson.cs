// -----------------------------------------------------------------------
// <copyright file="QdrantJson.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compendium.Adapters.Qdrant.Internal;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used by the Qdrant adapter. Qdrant's REST API
/// expects snake_case field names and emits the same.
/// </summary>
internal static class QdrantJson
{
    /// <summary>
    /// Serializer options for Qdrant requests + responses. Snake-case naming,
    /// case-insensitive deserialisation, ignore null on write.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
