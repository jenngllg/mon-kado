using JennGllg.Fr.MonKado.Back.Application.Common;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Common;

public class WishTextNormalizerTests
{
    [Fact]
    public void NormalizeName_WhenNameHasWhitespaceAndDecomposedCharacters_ReturnsCanonicalName()
    {
        // Arrange
        var name = "  Cafe\u0301  ";

        // Act
        var result = WishTextNormalizer.NormalizeName(name);

        // Assert
        Assert.Equal(
            "Café",
            result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeNote_WhenNoteIsBlank_ReturnsNull(string? note)
    {
        // Act
        var result = WishTextNormalizer.NormalizeNote(note);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void NormalizeNote_WhenNoteHasContent_ReturnsCanonicalTrimmedNote()
    {
        // Act
        var result = WishTextNormalizer.NormalizeNote("  Cafe\u0301  ");

        // Assert
        Assert.Equal(
            "Café",
            result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeUrl_WhenUrlIsBlank_ReturnsNull(string? url)
    {
        // Act
        var result = WishTextNormalizer.NormalizeUrl(url);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void NormalizeUrl_WhenUrlHasContent_ReturnsTrimmedUrl()
    {
        // Act
        var result = WishTextNormalizer.NormalizeUrl("  https://example.com/gift  ");

        // Assert
        Assert.Equal(
            "https://example.com/gift",
            result);
    }
}
