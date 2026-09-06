using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Options;

public class GiftImageStorageOptionsValidatorTests
{
    private readonly GiftImageStorageOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenStoragePathIsAbsolute_ReturnsSuccess()
    {
        // Arrange
        var options = new GiftImageStorageOptions
        {
            StoragePath = Path.GetFullPath("gift-images")
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_WhenStoragePathIsMissing_ReturnsFailure(string? storagePath)
    {
        // Arrange
        var options = new GiftImageStorageOptions
        {
            StoragePath = storagePath
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Single(result.Failures);
    }
}
