using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class WebSecurityExtensionsTests
{
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
    public void AddWebSecurity_WhenOriginIsInvalid_ThrowsInvalidOperationException(string origin)
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            origin,
            "localhost");

        // Act
        IServiceCollection action() => services.AddWebSecurity(
            configuration,
            new TestWebHostEnvironment("Local"));

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
    }

    [Theory]
    [InlineData("Production", "http://localhost:5173")]
    [InlineData("Local", "http://app.example.test")]
    public void AddWebSecurity_WhenOriginUsesUnsafeHttp_ThrowsInvalidOperationException(
        string environmentName,
        string origin)
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            origin,
            "localhost");

        // Act
        IServiceCollection action() => services.AddWebSecurity(
            configuration,
            new TestWebHostEnvironment(environmentName));

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
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
    public void AddWebSecurity_WhenAllowedHostIsInvalid_ThrowsInvalidOperationException(
        string? allowedHosts)
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            "https://app.example.test",
            allowedHosts);

        // Act
        IServiceCollection action() => services.AddWebSecurity(
            configuration,
            new TestWebHostEnvironment("Local"));

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
    }

    private static IConfiguration CreateConfiguration(
        string origin,
        string? allowedHosts)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = allowedHosts,
                ["WebSecurity:AllowedOrigins:0"] = origin
            })
            .Build();
    }
}
