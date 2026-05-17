// -----------------------------------------------------------------------
// <copyright file="QdrantOptionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using Compendium.Adapters.Qdrant.Options;

namespace Compendium.Adapters.Qdrant.Tests.Options;

/// <summary>
/// Verifies the configurable surface of <see cref="QdrantOptions"/> — defaults,
/// data-annotation validation, and the public section-name constant.
/// </summary>
public class QdrantOptionsTests
{
    [Fact]
    public void Defaults_AreSensibleForLocalDevelopment()
    {
        // Arrange / Act
        var options = new QdrantOptions();

        // Assert
        options.BaseUrl.Should().Be("http://localhost:6333");
        options.ApiKey.Should().BeNull();
        options.CollectionPrefix.Should().BeEmpty();
        options.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        options.DefaultIndex.Should().Be(QdrantIndexType.Hnsw);
        options.HnswM.Should().Be(16);
        options.HnswEfConstruct.Should().Be(128);
        options.WaitForUpsert.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://api.example.com", true)]
    [InlineData("http://localhost:6333", true)]
    [InlineData("", false)]
    [InlineData("not-a-url", false)]
    public void DataAnnotations_BaseUrl_ValidatesAsExpected(string baseUrl, bool expectedValid)
    {
        // Arrange
        var options = new QdrantOptions { BaseUrl = baseUrl };
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var actual = Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);

        // Assert
        actual.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1001)]
    public void DataAnnotations_HnswMOutOfRange_Rejected(int m)
    {
        // Arrange
        var options = new QdrantOptions { HnswM = m };
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var actual = Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);

        // Assert
        actual.Should().BeFalse();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(1001)]
    public void DataAnnotations_HnswEfConstructOutOfRange_Rejected(int ef)
    {
        // Arrange
        var options = new QdrantOptions { HnswEfConstruct = ef };
        var ctx = new ValidationContext(options);
        var results = new List<ValidationResult>();

        // Act
        var actual = Validator.TryValidateObject(options, ctx, results, validateAllProperties: true);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void SectionName_IsCanonical()
    {
        // Assert
        QdrantOptions.SectionName.Should().Be("Compendium:Adapters:Qdrant");
    }
}
