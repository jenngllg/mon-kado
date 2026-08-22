using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class JwtOptionsValidatorTests
{
    private const string SigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Fact]
    public void Validate_WhenConfigurationIsValid_ReturnsSuccess()
    {
        // Arrange
        var validator = new JwtOptionsValidator();
        var options = new JwtOptions
        {
            Audience = "MonKado.Frontend",
            Issuer = "MonKado.Api",
            SigningKey = SigningKey
        };

        // Act
        var result = validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenConfigurationIsMissing_ReturnsAllFailures()
    {
        // Arrange
        var validator = new JwtOptionsValidator();
        var options = new JwtOptions();

        // Act
        var result = validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Equal(
            3,
            result.Failures.Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHw==")]
    public void Validate_WhenSigningKeyIsInvalid_ReturnsFailure(string signingKey)
    {
        // Arrange
        var validator = new JwtOptionsValidator();
        var options = new JwtOptions
        {
            Audience = "MonKado.Frontend",
            Issuer = "MonKado.Api",
            SigningKey = signingKey
        };

        // Act
        var result = validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
    }
}
