using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class WishTextValidationTests
{
    [Theory]
    [InlineData("Cadeau")]
    [InlineData("  🎁  ")]
    public void IsValidName_WhenNameIsSupported_ReturnsTrue(string name)
    {
        // Act
        var result = WishTextValidation.IsValidName(name);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Cadeau\ninterdit")]
    [InlineData("Cadeau\u2028interdit")]
    [InlineData("Cadeau\u2029interdit")]
    [InlineData("Cadeau\u0001interdit")]
    public void IsValidName_WhenNameIsUnsupported_ReturnsFalse(string? name)
    {
        // Act
        var result = WishTextValidation.IsValidName(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidName_WhenNameExceedsMaximumUnicodeLength_ReturnsFalse()
    {
        // Arrange
        var name = string.Concat(
            Enumerable.Repeat(
                "🎁",
                WishTextValidation.MaximumNameLength + 1));

        // Act
        var result = WishTextValidation.IsValidName(name);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidName_WhenNameContainsUnpairedSurrogate_ReturnsFalse()
    {
        // Arrange
        var name = new string(
            (char)0xD800,
            1);

        // Act
        var result = WishTextValidation.IsValidName(name);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("Première ligne\nDeuxième ligne\tfin")]
    public void IsValidNote_WhenNoteIsSupported_ReturnsTrue(string? note)
    {
        // Act
        var result = WishTextValidation.IsValidNote(note);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidNote_WhenNoteExceedsMaximumUnicodeLength_ReturnsFalse()
    {
        // Arrange
        var note = new string(
            'a',
            WishTextValidation.MaximumNoteLength + 1);

        // Act
        var result = WishTextValidation.IsValidNote(note);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("Note\u0001")]
    [InlineData("Note\u0000")]
    public void IsValidNote_WhenNoteContainsUnsupportedControl_ReturnsFalse(string note)
    {
        // Act
        var result = WishTextValidation.IsValidNote(note);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidNote_WhenNoteContainsUnpairedSurrogate_ReturnsFalse()
    {
        // Arrange
        var note = new string(
            (char)0xD800,
            1);

        // Act
        var result = WishTextValidation.IsValidNote(note);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com/gift")]
    [InlineData("HTTPS://example.com/gift?q=1")]
    [InlineData("https://example.com/path@value")]
    public void IsValidUrl_WhenUrlIsSupported_ReturnsTrue(string? url)
    {
        // Act
        var result = WishTextValidation.IsValidUrl(url);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("gift")]
    [InlineData("ftp://example.com/gift")]
    [InlineData("https://@example.com/gift")]
    [InlineData("https://user:password@example.com/gift")]
    [InlineData("https:///gift")]
    [InlineData("https://example.com/a b")]
    public void IsValidUrl_WhenUrlIsUnsupported_ReturnsFalse(string url)
    {
        // Act
        var result = WishTextValidation.IsValidUrl(url);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidUrl_WhenUrlExceedsMaximumLength_ReturnsFalse()
    {
        // Arrange
        var url = "https://example.com/" + new string(
            'a',
            WishTextValidation.MaximumUrlLength);

        // Act
        var result = WishTextValidation.IsValidUrl(url);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidUrl_WhenUrlContainsUnpairedSurrogate_ReturnsFalse()
    {
        // Arrange
        var url = "https://example.com/" + new string(
            (char)0xD800,
            1);

        // Act
        var result = WishTextValidation.IsValidUrl(url);

        // Assert
        Assert.False(result);
    }
}
