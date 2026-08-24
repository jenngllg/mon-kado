using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Options;

public class GoogleAuthenticationOptionsValidatorTests
{
    private readonly GoogleAuthenticationOptionsValidator _validator = new(
        new GoogleReturnPathValidator(),
        Microsoft.Extensions.Options.Options.Create(new WebSecurityOptions
        {
            AllowedOrigins =
            [
                "https://app.example.test"
            ]
        }));

    [Fact]
    public void Validate_WhenProviderIsDisabled_ReturnsSuccessWithoutSecrets()
    {
        // Arrange
        var options = new GoogleAuthenticationOptions();

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenConfigurationIsValid_ReturnsSuccess()
    {
        // Arrange
        var options = CreateValidOptions();

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenAllowedReturnPathsAreMissing_ReturnsFailure()
    {
        // Arrange
        var options = new GoogleAuthenticationOptions
        {
            Enabled = true,
            ClientId = "client.apps.googleusercontent.com",
            ClientSecret = "client-secret",
            FrontendOrigin = "https://app.example.test",
            DefaultReturnPath = "/my-lists"
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "AllowedReturnPaths",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenDefaultReturnPathIsNotCanonical_ReturnsFailure()
    {
        // Arrange
        var valid = CreateValidOptions();
        var options = new GoogleAuthenticationOptions
        {
            Enabled = valid.Enabled,
            ClientId = valid.ClientId,
            ClientSecret = valid.ClientSecret,
            FrontendOrigin = valid.FrontendOrigin,
            DefaultReturnPath = "//evil.example",
            AllowedReturnPaths = valid.AllowedReturnPaths
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "DefaultReturnPath",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenConfigurationIsInvalid_ReturnsEveryFailure()
    {
        // Arrange
        var options = new GoogleAuthenticationOptions
        {
            Enabled = true,
            ClientId = " ",
            ClientSecret = null,
            FrontendOrigin = "http://app.example.test/path",
            DefaultReturnPath = "/missing",
            AllowedReturnPaths =
            [
                "/my-lists",
                "/my-lists",
                "https://evil.example"
            ]
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Equal(
            6,
            result.Failures.Count());
    }

    [Theory]
    [InlineData("https://app.example.test/")]
    [InlineData("https://user@app.example.test")]
    [InlineData("https://*.example.test")]
    [InlineData("http://localhost:5173")]
    [InlineData(null)]
    [InlineData("not-an-origin")]
    [InlineData("ftp://app.example.test")]
    [InlineData("https://app.example.test?value=1")]
    [InlineData("https://app.example.test#fragment")]
    public void Validate_WhenFrontendOriginIsInvalid_ReturnsFailure(string? origin)
    {
        // Arrange
        var valid = CreateValidOptions();
        var options = new GoogleAuthenticationOptions
        {
            Enabled = valid.Enabled,
            ClientId = valid.ClientId,
            ClientSecret = valid.ClientSecret,
            FrontendOrigin = origin,
            DefaultReturnPath = valid.DefaultReturnPath,
            AllowedReturnPaths = valid.AllowedReturnPaths
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_WhenFrontendOriginIsNotAllowedByCors_ReturnsFailure()
    {
        // Arrange
        var validator = new GoogleAuthenticationOptionsValidator(
            new GoogleReturnPathValidator(),
            Microsoft.Extensions.Options.Options.Create(new WebSecurityOptions
            {
                AllowedOrigins =
                [
                    "https://another.example.test"
                ]
            }));
        var options = CreateValidOptions();

        // Act
        var result = validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "WebSecurity:AllowedOrigins",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Validate_WhenBackchannelTimeoutIsOutsideBounds_ReturnsFailure(int timeoutSeconds)
    {
        // Arrange
        var valid = CreateValidOptions();
        var options = new GoogleAuthenticationOptions
        {
            Enabled = valid.Enabled,
            ClientId = valid.ClientId,
            ClientSecret = valid.ClientSecret,
            BackchannelTimeoutSeconds = timeoutSeconds,
            FrontendOrigin = valid.FrontendOrigin,
            DefaultReturnPath = valid.DefaultReturnPath,
            AllowedReturnPaths = valid.AllowedReturnPaths
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "BackchannelTimeoutSeconds",
                StringComparison.Ordinal));
    }

    private static GoogleAuthenticationOptions CreateValidOptions()
    {

        return new GoogleAuthenticationOptions
        {
            Enabled = true,
            ClientId = "client.apps.googleusercontent.com",
            ClientSecret = "client-secret",
            FrontendOrigin = "https://app.example.test",
            DefaultReturnPath = "/my-lists",
            AllowedReturnPaths =
            [
                "/my-lists"
            ]
        };
    }
}
