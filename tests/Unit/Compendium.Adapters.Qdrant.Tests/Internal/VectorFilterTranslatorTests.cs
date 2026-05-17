// -----------------------------------------------------------------------
// <copyright file="VectorFilterTranslatorTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant.Internal;

namespace Compendium.Adapters.Qdrant.Tests.Internal;

/// <summary>
/// Exercises <see cref="VectorFilterTranslator"/> — the layer that turns the
/// abstraction's <see cref="VectorFilter"/> into the Qdrant filter wire shape.
/// Every produced filter must enforce tenant scoping, even when the caller
/// passes <c>null</c>.
/// </summary>
public class VectorFilterTranslatorTests
{
    [Fact]
    public void Build_NullFilterAndNoTenant_ProducesMustNotOnSentinel()
    {
        // Arrange / Act
        var result = VectorFilterTranslator.Build(filter: null, tenantOverride: null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Must.Should().BeEmpty();
        result.Value.MustNot.Should().NotBeNull();
        result.Value.MustNot!.Should().ContainSingle().Which.Match.Should().NotBeNull();
        result.Value.MustNot[0].Key.Should().Be(VectorFilterTranslator.TenantPayloadKey);
    }

    [Fact]
    public void Build_NullFilterWithTenantOverride_EmitsTenantMustClause()
    {
        // Arrange / Act
        var result = VectorFilterTranslator.Build(filter: null, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Must.Should().ContainSingle();
        result.Value.Must![0].Key.Should().Be(VectorFilterTranslator.TenantPayloadKey);
        result.Value.Must[0].Match!.Value.Should().Be("tenant-1");
        result.Value.MustNot.Should().BeNull();
    }

    [Fact]
    public void Build_TenantOnFilterUsedWhenNoOverride()
    {
        // Arrange
        var filter = VectorFilter.Eq("category", "support").ForTenant("tenant-2");

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Must.Should().ContainSingle(c => c.Key == VectorFilterTranslator.TenantPayloadKey && (string?)c.Match!.Value == "tenant-2");
    }

    [Fact]
    public void Build_TenantOverrideTakesPrecedence()
    {
        // Arrange
        var filter = VectorFilter.Eq("category", "support").ForTenant("tenant-on-filter");

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-override");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Must.Should().ContainSingle(c => c.Key == VectorFilterTranslator.TenantPayloadKey && (string?)c.Match!.Value == "tenant-override");
    }

    [Fact]
    public void Build_InvalidTenant_ReturnsValidation()
    {
        // Arrange / Act
        var result = VectorFilterTranslator.Build(filter: null, tenantOverride: "bad tenant");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidTenantId");
    }

    [Fact]
    public void Build_EqFilter_ProducesKeyValueMatch()
    {
        // Arrange
        var filter = VectorFilter.Eq("category", "support");

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        // Must clause contains: [0] tenant, [1] the eq node.
        result.Value!.Must.Should().HaveCount(2);
        result.Value.Must![1].Key.Should().Be("category");
        result.Value.Must[1].Match!.Value.Should().Be("support");
    }

    [Fact]
    public void Build_NeFilter_ProducesNestedMustNot()
    {
        // Arrange
        var filter = VectorFilter.Ne("category", "spam");

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Must.Should().HaveCount(2);
        var ne = result.Value.Must![1];
        ne.Filter.Should().NotBeNull();
        ne.Filter!.MustNot.Should().ContainSingle()
            .Which.Match!.Value.Should().Be("spam");
    }

    [Fact]
    public void Build_InFilter_ProducesAnyMatch()
    {
        // Arrange
        var filter = VectorFilter.In("tag", new object[] { "a", "b", "c" });

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Must.Should().HaveCount(2);
        var inNode = result.Value.Must![1];
        inNode.Key.Should().Be("tag");
        inNode.Match!.Any.Should().BeEquivalentTo(new object[] { "a", "b", "c" });
    }

    [Fact]
    public void VectorFilter_EmptyIn_ThrowsAtConstruction()
    {
        // Arrange / Act
        var act = () => VectorFilter.In("tag", Array.Empty<object>());

        // Assert — the abstraction rejects empty In() at construction, so the
        // translator's defensive branch is unreachable from the public API.
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_RangeFilterInclusive_MapsToGteAndLte()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: 10, max: 50, minInclusive: true, maxInclusive: true);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var range = result.Value!.Must![1].Range!;
        range.Gte.Should().Be(10);
        range.Lte.Should().Be(50);
        range.Gt.Should().BeNull();
        range.Lt.Should().BeNull();
    }

    [Fact]
    public void Build_RangeFilterExclusive_MapsToGtAndLt()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: 10, max: 50, minInclusive: false, maxInclusive: false);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var range = result.Value!.Must![1].Range!;
        range.Gt.Should().Be(10);
        range.Lt.Should().Be(50);
        range.Gte.Should().BeNull();
        range.Lte.Should().BeNull();
    }

    [Fact]
    public void VectorFilter_RangeNoBounds_ThrowsAtConstruction()
    {
        // Arrange / Act — abstraction rejects no-bound Range() at construction.
        var act = () => VectorFilter.Range("score", min: null, max: null, minInclusive: true, maxInclusive: true);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_AndFilter_ProducesNestedMust()
    {
        // Arrange
        var filter = VectorFilter.And(
            VectorFilter.Eq("category", "support"),
            VectorFilter.Eq("status", "open"));

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var andNode = result.Value!.Must![1];
        andNode.Filter.Should().NotBeNull();
        andNode.Filter!.Must.Should().HaveCount(2);
    }

    [Fact]
    public void Build_OrFilter_ProducesNestedShould()
    {
        // Arrange
        var filter = VectorFilter.Or(
            VectorFilter.Eq("category", "support"),
            VectorFilter.Eq("category", "sales"));

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        var orNode = result.Value!.Must![1];
        orNode.Filter.Should().NotBeNull();
        orNode.Filter!.Should.Should().HaveCount(2);
        orNode.Filter.Must.Should().BeNull();
    }

    [Fact]
    public void VectorFilter_EmptyAnd_ThrowsAtConstruction()
    {
        // Arrange / Act
        var act = () => VectorFilter.And();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VectorFilter_EmptyOr_ThrowsAtConstruction()
    {
        // Arrange / Act
        var act = () => VectorFilter.Or();

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("'oops")]
    [InlineData("\"oops")]
    [InlineData("with\\backslash")]
    [InlineData("with\nnewline")]
    public void Build_InvalidFieldName_ReturnsValidation(string field)
    {
        // Arrange
        var filter = VectorFilter.Eq(field, "v");

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidFilterField");
    }

    [Theory]
    [InlineData(42, 42L)]
    [InlineData(true, true)]
    [InlineData("plain", "plain")]
    public void Build_EqValueIsCoercedToWireTypes(object input, object expected)
    {
        // Arrange
        var filter = VectorFilter.Eq("key", input);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Must![1].Match!.Value.Should().Be(expected);
    }

    [Fact]
    public void Build_EqLongValue_Preserved()
    {
        // Arrange
        var filter = VectorFilter.Eq("key", 9_000_000_000L);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Match!.Value.Should().Be(9_000_000_000L);
    }

    [Fact]
    public void Build_EqFloatValue_ConvertedToDouble()
    {
        // Arrange
        var filter = VectorFilter.Eq("key", 3.14f);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Match!.Value.Should().BeOfType<double>();
    }

    [Fact]
    public void Build_EqDoubleValue_Preserved()
    {
        // Arrange
        var filter = VectorFilter.Eq("key", 3.14);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Match!.Value.Should().Be(3.14);
    }

    [Fact]
    public void Build_EqDecimalValue_ConvertedToDouble()
    {
        // Arrange
        var filter = VectorFilter.Eq("key", 3.14m);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Match!.Value.Should().BeOfType<double>();
    }

    [Fact]
    public void Build_EqCustomObjectValue_FallsBackToToString()
    {
        // Arrange
        var custom = new System.Text.StringBuilder("hello");
        var filter = VectorFilter.Eq("key", custom);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Match!.Value.Should().Be("hello");
    }

    [Fact]
    public void Build_RangeWithIntBounds_ConvertedToDouble()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: 10, max: 50, minInclusive: true, maxInclusive: true);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Range!.Gte.Should().Be(10);
        result.Value.Must[1].Range!.Lte.Should().Be(50);
    }

    [Fact]
    public void Build_RangeWithLongBounds_ConvertedToDouble()
    {
        // Arrange
        var filter = VectorFilter.Range(
            "score",
            min: 10_000_000_000L,
            max: 20_000_000_000L,
            minInclusive: true,
            maxInclusive: true);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Range!.Gte.Should().Be(10_000_000_000d);
        result.Value.Must[1].Range!.Lte.Should().Be(20_000_000_000d);
    }

    [Fact]
    public void Build_RangeWithFloatBounds_ConvertedToDouble()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: 1.5f, max: 9.5f, minInclusive: true, maxInclusive: true);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Range!.Gte.Should().BeApproximately(1.5, 1e-6);
    }

    [Fact]
    public void Build_RangeWithDoubleBounds_ConvertedToDouble()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: 1.5, max: 9.5, minInclusive: true, maxInclusive: true);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Range!.Gte.Should().Be(1.5);
    }

    [Fact]
    public void Build_RangeWithDecimalBounds_ConvertedToDouble()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: 1.5m, max: 9.5m, minInclusive: true, maxInclusive: true);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Range!.Gte.Should().Be(1.5);
    }

    [Fact]
    public void Build_RangeWithStringNumericBounds_Parsed()
    {
        // Arrange
        var filter = VectorFilter.Range("score", min: "1.5", max: "9.5", minInclusive: true, maxInclusive: true);

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.Value!.Must![1].Range!.Gte.Should().Be(1.5);
    }

    [Fact]
    public void Build_NeFilterInvalidField_ReturnsValidation()
    {
        // Arrange
        var filter = VectorFilter.Ne("'bad", "v");

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidFilterField");
    }

    [Fact]
    public void Build_AndPropagatesChildFailure()
    {
        // Arrange — outer And contains a child with an invalid field name.
        var filter = VectorFilter.And(
            VectorFilter.Eq("ok", "v"),
            VectorFilter.Eq("'bad", "v"));

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidFilterField");
    }

    [Fact]
    public void Build_OrPropagatesChildFailure()
    {
        // Arrange
        var filter = VectorFilter.Or(
            VectorFilter.Eq("ok", "v"),
            VectorFilter.Eq("'bad", "v"));

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidFilterField");
    }

    [Fact]
    public void Build_FieldNameTooLong_ReturnsValidation()
    {
        // Arrange — field over 128 chars.
        var filter = VectorFilter.Eq(new string('a', 200), "v");

        // Act
        var result = VectorFilterTranslator.Build(filter, tenantOverride: "tenant-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Qdrant.InvalidFilterField");
    }
}
