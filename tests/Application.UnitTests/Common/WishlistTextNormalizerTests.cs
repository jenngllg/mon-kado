using JennGllg.Fr.MonKado.Back.Application.Common;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Common;

public class WishlistTextNormalizerTests
{
    [Fact]
    public void NormalizeName_WhenNameContainsWhitespaceAndDecomposedUnicode_ReturnsNormalizedName()
    {
        // Arrange
        const string Name = "  Liste de Le\u0301a  ";

        // Act
        var result = WishlistTextNormalizer.NormalizeName(Name);

        // Assert
        Assert.Equal(
            "Liste de Léa",
            result);
    }

    [Fact]
    public void NormalizeNameForUniqueness_WhenNameContainsLowercase_ReturnsInvariantUppercase()
    {
        // Arrange
        const string Name = "Liste de Le\u0301a";

        // Act
        var result = WishlistTextNormalizer.NormalizeNameForUniqueness(Name);

        // Assert
        Assert.Equal(
            "LISTE DE LÉA",
            result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeMessage_WhenMessageIsBlank_ReturnsNull(string? message)
    {
        // Arrange

        // Act
        var result = WishlistTextNormalizer.NormalizeMessage(message);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void NormalizeMessage_WhenMessageContainsContent_ReturnsTrimmedNormalizedMessage()
    {
        // Arrange
        const string Message = "  Merci Le\u0301a  ";

        // Act
        var result = WishlistTextNormalizer.NormalizeMessage(Message);

        // Assert
        Assert.Equal(
            "Merci Léa",
            result);
    }
}
