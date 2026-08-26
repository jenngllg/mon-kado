using JennGllg.Fr.MonKado.Back.Api.Options;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Options;

public class WishlistSharingOptionsValidatorTests
{
    [Fact]
    public void Validate_WhenHttpsOriginIsCanonical_ReturnsSuccess()
    {
        // Arrange
        var validator = new WishlistSharingOptionsValidator(new TestWebHostEnvironment("Production"));

        // Act
        var result = validator.Validate(
            null,
            new WishlistSharingOptions
            {
                FrontendOrigin = "https://app.example.test"
            });

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_WhenOriginIsMissing_ReturnsFailure(string? origin)
    {
        // Arrange
        var validator = new WishlistSharingOptionsValidator(new TestWebHostEnvironment("Local"));

        // Act
        var result = validator.Validate(
            null,
            new WishlistSharingOptions
            {
                FrontendOrigin = origin
            });

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(
            "required",
            result.FailureMessage,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://app.example.test/path")]
    [InlineData("http://app.example.test")]
    public void Validate_WhenOriginViolatesEnvironmentPolicy_ReturnsFailure(string origin)
    {
        // Arrange
        var validator = new WishlistSharingOptionsValidator(new TestWebHostEnvironment("Production"));

        // Act
        var result = validator.Validate(
            null,
            new WishlistSharingOptions
            {
                FrontendOrigin = origin
            });

        // Assert
        Assert.True(result.Failed);
    }
}
