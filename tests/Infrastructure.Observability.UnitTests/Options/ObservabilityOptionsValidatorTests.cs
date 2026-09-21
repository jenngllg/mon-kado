using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Options;

public class ObservabilityOptionsValidatorTests
{
    private readonly ObservabilityOptionsValidator _validator = new();

    [Theory]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz", false)]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("local", true)]
    public void Validate_WhenRevisionProvided_OnlyAcceptsLocalOrGitRevision(
        string version,
        bool valid)
    {
        // Arrange
        var options = new ObservabilityOptions { Version = version };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.Equal(
            valid,
            result.Succeeded);
    }

    [Theory]
    [InlineData("api", 5, true)]
    [InlineData("worker", 60, true)]
    [InlineData("other", 30, false)]
    [InlineData("api", 4, false)]
    [InlineData("api", 61, false)]
    public void Validate_WhenConfigurationProvided_EnforcesBounds(
        string service,
        int seconds,
        bool valid)
    {
        // Arrange
        var options = new ObservabilityOptions
        {
            Enabled = true,
            Service = service,
            Interval = TimeSpan.FromSeconds(seconds),
            Directory = Path.GetTempPath()
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData(false, "", true)]
    [InlineData(true, "", false)]
    [InlineData(true, "relative", false)]
    public void Validate_WhenSnapshotsDisabled_DoesNotRequirePlatformSpecificDirectory(
        bool enabled,
        string directory,
        bool valid)
    {
        // Arrange
        var options = new ObservabilityOptions { Enabled = enabled, Directory = directory };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.Equal(valid, result.Succeeded);
    }
}
