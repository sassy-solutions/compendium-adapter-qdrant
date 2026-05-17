// -----------------------------------------------------------------------
// <copyright file="MetadataSerializerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Compendium.Adapters.Qdrant.Internal;

namespace Compendium.Adapters.Qdrant.Tests.Internal;

public class MetadataSerializerTests
{
    [Fact]
    public void ToPayload_NullMetadata_ReturnsEmptyDictionary()
    {
        // Arrange / Act
        var actual = MetadataSerializer.ToPayload(null);

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void ToPayload_EmptyMetadata_ReturnsEmptyDictionary()
    {
        // Arrange / Act
        var actual = MetadataSerializer.ToPayload(new Dictionary<string, object>());

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void ToPayload_CopiesEntries()
    {
        // Arrange
        var metadata = new Dictionary<string, object>
        {
            ["title"] = "alpha",
            ["score"] = 42,
        };

        // Act
        var actual = MetadataSerializer.ToPayload(metadata);

        // Assert
        actual["title"].Should().Be("alpha");
        actual["score"].Should().Be(42);
    }

    [Fact]
    public void FromPayload_StripsTenantKey()
    {
        // Arrange
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "alpha",
            [VectorFilterTranslator.TenantPayloadKey] = "tenant-1",
        };

        // Act
        var actual = MetadataSerializer.FromPayload(payload);

        // Assert
        actual.Should().ContainKey("title");
        actual.Should().NotContainKey(VectorFilterTranslator.TenantPayloadKey);
    }

    [Fact]
    public void FromPayload_NullPayload_ReturnsEmpty()
    {
        // Arrange / Act
        var actual = MetadataSerializer.FromPayload(null);

        // Assert
        actual.Should().BeEmpty();
    }

    [Fact]
    public void FromPayload_UnwrapsJsonElementValues()
    {
        // Arrange — System.Text.Json deserialises 'object' as JsonElement.
        using var doc = JsonDocument.Parse("""{"s":"x","n":42,"b":true,"arr":[1,2],"obj":{"k":"v"}}""");
        var payload = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            payload[prop.Name] = prop.Value;
        }

        // Act
        var actual = MetadataSerializer.FromPayload(payload);

        // Assert
        actual["s"].Should().Be("x");
        actual["n"].Should().Be((long)42);
        actual["b"].Should().Be(true);
        ((object[])actual["arr"]).Should().HaveCount(2);
        ((Dictionary<string, object>)actual["obj"]).Should().ContainKey("k");
    }

    [Fact]
    public void FromPayload_DropsNullValues()
    {
        // Arrange
        var payload = new Dictionary<string, object?> { ["nil"] = null, ["keep"] = "x" };

        // Act
        var actual = MetadataSerializer.FromPayload(payload);

        // Assert
        actual.Should().NotContainKey("nil");
        actual.Should().ContainKey("keep");
    }
}
