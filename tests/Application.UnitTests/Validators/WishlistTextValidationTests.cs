using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class WishlistTextValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidName_WhenValueIsBlank_ReturnsFalse(string? value)
    {
        // Arrange

        // Act
        var result = WishlistTextValidation.IsValidName(value);

        // Assert
        Assert.False(result);
    }
}
