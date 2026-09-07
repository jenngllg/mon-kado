using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.UnitTests.Options;

public class PersonalDataExportStorageOptionsValidatorTests
{
    private readonly PersonalDataExportStorageOptionsValidator _validator = new();
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid\0path")]
    public void Validate_WhenPathIsInvalid_RejectsConfiguration(string? path)
    {
        // Arrange
        var options = new PersonalDataExportStorageOptions
        {
            StoragePath = path
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_WhenPathResolvesToFilesystemRoot_RejectsConfiguration()
    {
        // Arrange
        var options = new PersonalDataExportStorageOptions
        {
            StoragePath = Path.GetPathRoot(Path.GetTempPath())
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_WhenPathIsPrivateDirectory_AcceptsRelativeAndAbsoluteLocalPaths(bool absolute)
    {
        // Arrange
        var path = absolute ? Path.GetFullPath(".local/export-options-test") : ".local/export-options-test";
        var options = new PersonalDataExportStorageOptions
        {
            StoragePath = path
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }
}
