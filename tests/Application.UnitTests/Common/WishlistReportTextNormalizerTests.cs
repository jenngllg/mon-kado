using JennGllg.Fr.MonKado.Back.Application.Common;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Common;

public class WishlistReportTextNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeDetails_WhenDetailsAreBlank_ReturnsNull(string? details)
    {
        // Arrange

        // Act
        var result = WishlistReportTextNormalizer.NormalizeDetails(details);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void NormalizeDetails_WhenDetailsContainContent_ReturnsTrimmedNormalizedText()
    {
        // Arrange
        const string Details = "  De\u0301tails  ";

        // Act
        var result = WishlistReportTextNormalizer.NormalizeDetails(Details);

        // Assert
        Assert.Equal(
            "Détails",
            result);
    }
}
