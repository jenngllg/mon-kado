using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Options;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class WebSecurityValidationTests
{
    [Fact]
    public void ValidateConfiguration_WhenConfigurationIsValid_DoesNotThrow()
    {
        // Arrange
        var options = new WebSecurityOptions
        {
            AllowedOrigins =
            [
                "https://app.example.test",
                "http://localhost:5173"
            ]
        };
        var environment = new TestWebHostEnvironment("Local");

        // Act
        var exception = Record.Exception(() => WebSecurityExtensions.ValidateConfiguration(
            options,
            "api.example.test;localhost",
            environment));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConfiguration_WhenNoOriginExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new WebSecurityOptions();
        var environment = new TestWebHostEnvironment("Local");

        // Act
        void action() => WebSecurityExtensions.ValidateConfiguration(
            options,
            "localhost",
            environment);

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("not-an-origin")]
    [InlineData("ftp://app.example.test")]
    [InlineData("https://user@app.example.test")]
    [InlineData("https://app.example.test/path")]
    [InlineData("https://app.example.test?query=value")]
    [InlineData("https://app.example.test#fragment")]
    [InlineData("https://app.example.test/")]
    public void ValidateOrigin_WhenOriginIsInvalid_ThrowsInvalidOperationException(string origin)
    {
        // Arrange
        var environment = new TestWebHostEnvironment("Local");

        // Act
        void action() => WebSecurityExtensions.ValidateOrigin(
            origin,
            environment);

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void ValidateOrigin_WhenProductionOriginUsesHttp_ThrowsInvalidOperationException()
    {
        // Arrange
        var environment = new TestWebHostEnvironment("Production");

        // Act
        void action() => WebSecurityExtensions.ValidateOrigin(
            "http://localhost:5173",
            environment);

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void ValidateOrigin_WhenLocalNonLoopbackOriginUsesHttp_ThrowsInvalidOperationException()
    {
        // Arrange
        var environment = new TestWebHostEnvironment("Local");

        // Act
        void action() => WebSecurityExtensions.ValidateOrigin(
            "http://app.example.test",
            environment);

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    [InlineData("https://api.example.test")]
    [InlineData("api.example.test/path")]
    [InlineData("api.example.test:443")]
    public void ValidateAllowedHosts_WhenHostIsInvalid_ThrowsInvalidOperationException(string? hosts)
    {
        // Arrange
        // Act
        void action() => WebSecurityExtensions.ValidateAllowedHosts(hosts);

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }
}
