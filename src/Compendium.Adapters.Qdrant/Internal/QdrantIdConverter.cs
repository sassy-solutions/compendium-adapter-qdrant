// -----------------------------------------------------------------------
// <copyright file="QdrantIdConverter.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compendium.Adapters.Qdrant.Internal;

/// <summary>
/// Reads Qdrant point ids — which the server emits as either a string (UUID-shaped) or
/// an unsigned integer — into a single .NET <see cref="string"/>. Writing always
/// emits a string, which Qdrant accepts in both UUID-form and arbitrary-string-form.
/// </summary>
internal sealed class QdrantIdConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString() ?? string.Empty;
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var l))
                {
                    return l.ToString(CultureInfo.InvariantCulture);
                }

                return reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.Null:
                return string.Empty;
            default:
                throw new JsonException($"Unexpected Qdrant id token: {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value);
    }
}
