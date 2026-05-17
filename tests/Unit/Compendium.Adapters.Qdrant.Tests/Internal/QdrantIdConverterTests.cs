// -----------------------------------------------------------------------
// <copyright file="QdrantIdConverterTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Compendium.Adapters.Qdrant.Internal;

namespace Compendium.Adapters.Qdrant.Tests.Internal;

public class QdrantIdConverterTests
{
    private sealed class Holder
    {
        [System.Text.Json.Serialization.JsonConverter(typeof(QdrantIdConverter))]
        public string Id { get; set; } = string.Empty;
    }

    [Fact]
    public void Read_StringId_ReturnsString()
    {
        // Arrange
        const string json = """{"Id":"abc-123"}""";

        // Act
        var actual = JsonSerializer.Deserialize<Holder>(json);

        // Assert
        actual!.Id.Should().Be("abc-123");
    }

    [Fact]
    public void Read_NumericId_StringifiesIt()
    {
        // Arrange
        const string json = """{"Id":42}""";

        // Act
        var actual = JsonSerializer.Deserialize<Holder>(json);

        // Assert
        actual!.Id.Should().Be("42");
    }

    [Fact]
    public void Read_FloatingPointId_StringifiesViaDouble()
    {
        // Arrange — exercise the non-int branch of the converter.
        var bytes = System.Text.Encoding.UTF8.GetBytes("3.14");
        var reader = new Utf8JsonReader(bytes);
        reader.Read();

        // Act
        var converter = new QdrantIdConverter();
        var actual = converter.Read(ref reader, typeof(string), new JsonSerializerOptions());

        // Assert
        actual.Should().StartWith("3.14");
    }

    [Fact]
    public void Read_NullTokenDirectly_ReturnsEmptyString()
    {
        // Arrange — exercise the JsonTokenType.Null branch directly. STJ skips
        // a custom converter for null tokens unless the converter opts in via
        // HandleNull, so we drive the reader by hand.
        var bytes = System.Text.Encoding.UTF8.GetBytes("null");
        var reader = new Utf8JsonReader(bytes);
        reader.Read(); // advance to Null token

        // Act
        var converter = new QdrantIdConverter();
        var actual = converter.Read(ref reader, typeof(string), new JsonSerializerOptions());

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void Read_UnexpectedToken_Throws()
    {
        // Arrange
        const string json = """{"Id":true}""";

        // Act
        var act = () => JsonSerializer.Deserialize<Holder>(json);

        // Assert
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Write_String_EmitsString()
    {
        // Arrange
        var holder = new Holder { Id = "x" };

        // Act
        var actual = JsonSerializer.Serialize(holder);

        // Assert
        actual.Should().Contain("\"Id\":\"x\"");
    }
}
