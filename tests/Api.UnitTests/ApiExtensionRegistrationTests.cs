using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class ApiExtensionRegistrationTests
{
    [Fact]
    public void AddApiHealthChecks_WhenCalled_ReturnsServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddApiHealthChecks();

        // Assert
        Assert.Same(
            services,
            result);
    }

    [Fact]
    public void AddApiOpenApi_WhenCalled_ReturnsServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddApiOpenApi();

        // Assert
        Assert.Same(
            services,
            result);
    }

    [Fact]
    public void AddIdentityAuthentication_WhenCalled_ReturnsServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var environment = new TestWebHostEnvironment("Local");

        // Act
        var result = services.AddIdentityAuthentication(environment);

        // Assert
        Assert.Same(
            services,
            result);
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("Production")]
    public void AddWebSecurity_WhenConfigurationIsValid_ReturnsServices(string environmentName)
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "api.example.test",
                ["WebSecurity:AllowedOrigins:0"] = "https://app.example.test"
            })
            .Build();
        var environment = new TestWebHostEnvironment(environmentName);

        // Act
        var result = services.AddWebSecurity(
            configuration,
            environment);
        using var provider = services.BuildServiceProvider();
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var cookieOptions = provider.GetRequiredService<IOptions<CookiePolicyOptions>>().Value;
        var antiforgeryOptions = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        // Assert
        Assert.Same(
            services,
            result);
        Assert.NotNull(corsOptions.GetPolicy(WebSecurityExtensions.FrontendCorsPolicy));
        Assert.NotEqual(
            default,
            cookieOptions.HttpOnly);
        Assert.NotNull(antiforgeryOptions.HeaderName);
    }

    [Fact]
    public void AddWebSecurity_WhenSectionIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "api.example.test"
            })
            .Build();

        // Act
        IServiceCollection action() => services.AddWebSecurity(
            configuration,
            new TestWebHostEnvironment("Local"));

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
    }

    [Theory]
    [InlineData("Local", null)]
    [InlineData("Local", "127.0.0.0/8")]
    [InlineData("Production", "127.0.0.0/8")]
    public void AddTrustedReverseProxy_WhenConfigurationIsValid_ReturnsServices(
        string environmentName,
        string? network)
    {
        // Arrange
        var services = new ServiceCollection();
        var values = new Dictionary<string, string?>();

        if (network is not null)
            values["ReverseProxy:KnownNetworks:0"] = network;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var environment = new TestWebHostEnvironment(environmentName);

        // Act
        var result = services.AddTrustedReverseProxy(
            configuration,
            environment);

        // Assert
        Assert.Same(
            services,
            result);
    }

    [Fact]
    public void AddTrustedReverseProxy_WhenProductionHasNoNetwork_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestWebHostEnvironment("Production");

        // Act
        IServiceCollection action() => services.AddTrustedReverseProxy(
            configuration,
            environment);

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
    }
}
