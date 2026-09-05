using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class WishlistReportTextValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Details\r\nwith\ttabs")]
    public void IsValidDetails_WhenValueIsSupported_ReturnsTrue(string? value)
    {
        // Arrange

        // Act
        var result = WishlistReportTextValidation.IsValidDetails(value);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidDetails_WhenValueExceedsMaximumLength_ReturnsFalse()
    {
        // Arrange
        var value = new string(
            'a',
            WishlistReportTextValidation.MaximumDetailsLength + 1);

        // Act
        var result = WishlistReportTextValidation.IsValidDetails(value);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidDetails_WhenValueContainsUnsupportedControl_ReturnsFalse()
    {
        // Arrange
        const string Value = "Details\u0001";

        // Act
        var result = WishlistReportTextValidation.IsValidDetails(Value);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidDetails_WhenValueContainsUnpairedSurrogate_ReturnsFalse()
    {
        // Arrange
        const string Value = "Details\ud800";

        // Act
        var result = WishlistReportTextValidation.IsValidDetails(Value);

        // Assert
        Assert.False(result);
    }
}
