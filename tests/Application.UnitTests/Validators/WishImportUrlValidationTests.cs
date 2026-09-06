using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class WishImportUrlValidationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("relative/path", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("https://user:secret@example.com", false)]
    [InlineData("https://example.com:8080", false)]
    [InlineData("http://example.com:443", false)]
    [InlineData("https://example.com/product?a=b", true)]
    [InlineData("http://example.com/product", true)]
    [InlineData("https://1.1.1.1/product", true)]
    [InlineData("https://[2606:4700:4700::1111]/product", true)]
    public void IsValid_WhenUrlIsChecked_ReturnsExpectedResult(
        string? url,
        bool expected)
    {
        // Arrange
        var candidate = url;

        // Act
        var actual = WishImportUrlValidation.IsValid(candidate);

        // Assert
        Assert.Equal(
            expected,
            actual);
    }
}
