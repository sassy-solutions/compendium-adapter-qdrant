// -----------------------------------------------------------------------
// <copyright file="DistanceMetricMapTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.VectorStore.Models;
using Compendium.Adapters.Qdrant.Internal;

namespace Compendium.Adapters.Qdrant.Tests.Internal;

public class DistanceMetricMapTests
{
    [Theory]
    [InlineData(DistanceMetric.Cosine, "Cosine")]
    [InlineData(DistanceMetric.L2, "Euclid")]
    [InlineData(DistanceMetric.InnerProduct, "Dot")]
    public void Label_MapsKnownMetricsToQdrantLabels(DistanceMetric metric, string expected)
    {
        // Arrange / Act
        var actual = DistanceMetricMap.Label(metric);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void Label_UnknownMetric_Throws()
    {
        // Arrange
        var bogus = (DistanceMetric)999;

        // Act
        var act = () => DistanceMetricMap.Label(bogus);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("Cosine", DistanceMetric.Cosine, true)]
    [InlineData("Euclid", DistanceMetric.L2, true)]
    [InlineData("Dot", DistanceMetric.InnerProduct, true)]
    public void TryParseLabel_KnownLabel_RoundTrips(string label, DistanceMetric expected, bool expectedReturn)
    {
        // Arrange / Act
        var actualReturn = DistanceMetricMap.TryParseLabel(label, out var actual);

        // Assert
        actualReturn.Should().Be(expectedReturn);
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Manhattan")]
    [InlineData("cosine")] // case-sensitive — Qdrant emits "Cosine"
    public void TryParseLabel_UnknownLabel_ReturnsFalse(string? label)
    {
        // Arrange / Act
        var actualReturn = DistanceMetricMap.TryParseLabel(label, out _);

        // Assert
        actualReturn.Should().BeFalse();
    }
}
