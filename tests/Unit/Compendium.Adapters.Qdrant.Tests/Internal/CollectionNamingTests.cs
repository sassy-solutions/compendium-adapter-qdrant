// -----------------------------------------------------------------------
// <copyright file="CollectionNamingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Qdrant.Internal;
using Compendium.Adapters.Qdrant.Options;

namespace Compendium.Adapters.Qdrant.Tests.Internal;

public class CollectionNamingTests
{
    [Theory]
    [InlineData("documents", true)]
    [InlineData("my_collection", true)]
    [InlineData("with-dash", true)]
    [InlineData("a", true)]
    public void IsValid_AcceptsSafeNames(string name, bool expected)
    {
        // Arrange / Act
        var actual = CollectionNaming.IsValid(name);

        // Assert
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("with/slash")]
    [InlineData("with;semi")]
    [InlineData("with'quote")]
    public void IsValid_RejectsUnsafeNames(string? name)
    {
        // Arrange / Act
        var actual = CollectionNaming.IsValid(name);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void IsValid_RejectsNameLongerThanMax()
    {
        // Arrange
        var name = new string('a', CollectionNaming.MaxLength + 1);

        // Act
        var actual = CollectionNaming.IsValid(name);

        // Assert
        actual.Should().BeFalse();
    }

    [Fact]
    public void Resolve_NoPrefix_ReturnsCollectionAsIs()
    {
        // Arrange
        var options = new QdrantOptions { CollectionPrefix = string.Empty };

        // Act
        var actual = CollectionNaming.Resolve(options, "documents");

        // Assert
        actual.Should().Be("documents");
    }

    [Fact]
    public void Resolve_WithPrefix_PrependsPrefix()
    {
        // Arrange
        var options = new QdrantOptions { CollectionPrefix = "dev_" };

        // Act
        var actual = CollectionNaming.Resolve(options, "documents");

        // Assert
        actual.Should().Be("dev_documents");
    }

    [Fact]
    public void Resolve_NullOptions_Throws()
    {
        // Arrange / Act
        var act = () => CollectionNaming.Resolve(null!, "documents");

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
