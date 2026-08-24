using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class GoogleReturnPathValidatorTests
{
    private readonly GoogleReturnPathValidator _validator = new();

    [Theory]
    [InlineData("/")]
    [InlineData("/my-lists")]
    [InlineData("/members/123")]
    public void IsCanonical_WhenPathIsCanonical_ReturnsTrue(string returnPath)
    {
        // Arrange

        // Act
        var result = _validator.IsCanonical(returnPath);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("my-lists")]
    [InlineData("//evil.example/path")]
    [InlineData("/my-lists/")]
    [InlineData("/my//lists")]
    [InlineData("/my-lists?next=/")]
    [InlineData("/my-lists#fragment")]
    [InlineData("/my-lists%2Fother")]
    [InlineData("/my\\lists")]
    [InlineData("/my/../lists")]
    [InlineData("/my/./lists")]
    [InlineData("/my lists")]
    [InlineData("/my\u0001lists")]
    public void IsCanonical_WhenPathIsUnsafe_ReturnsFalse(string? returnPath)
    {
        // Arrange

        // Act
        var result = _validator.IsCanonical(returnPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsCanonical_WhenPathExceedsMaximumLength_ReturnsFalse()
    {
        // Arrange
        var returnPath = string.Concat(
            "/",
            new string(
                'a',
                GoogleReturnPathValidation.MaximumLength));

        // Act
        var result = _validator.IsCanonical(returnPath);

        // Assert
        Assert.False(result);
    }
}
