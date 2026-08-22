using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Worker.Configurations;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class AuthenticationEmailConfigurationTests
{
    [Fact]
    public void Configure_WhenConfigurationIsEmpty_UsesDisabledDefaults()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();

        // Act
        var result = services.ConfigureAuthenticationEmailDelivery(
            configuration,
            new TestHostEnvironment("Local"));

        // Assert
        Assert.Same(
            services,
            result);
    }

    [Fact]
    public void ConfigureWorkerInjection_WhenConfigurationIsValid_RegistersWorkers()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        var services = new ServiceCollection();

        // Act
        var result = services.ConfigureWorkerInjection(
            configuration,
            new TestHostEnvironment("Local"));

        // Assert
        Assert.Same(
            services,
            result);
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType ==
                typeof(AuthenticationEmailDeliveryWorker));
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType ==
                typeof(UnconfirmedAccountCleanupWorker));
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType ==
                typeof(ExpiredAuthenticationSessionCleanupWorker));
    }

    [Fact]
    public void Configure_WhenLocalEnvironment_AllowsDisabledDeliveryWithoutGmailSecrets()
    {
        // Arrange
        // Act
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:Provider"] = "Disabled";
        var services = new ServiceCollection();

        var result = services.ConfigureAuthenticationEmailDelivery(
            configuration,
            new TestHostEnvironment("Local"));

        // Assert
        Assert.Same(
            services,
            result);
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType == typeof(AuthenticationEmailDeliveryWorker));
    }

    [Fact]
    public void Configure_WhenProduction_RejectsDisabledDelivery()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:Provider"] = "Disabled";
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Production")));

        // Assert
        Assert.Contains(
            "cannot be disabled",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WhenGmail_RequiresHttpsFrontendAndAllOAuthSecretsInProduction()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:Provider"] = "Gmail";
        configuration["AuthenticationEmail:FrontendOrigin"] = "http://localhost:5173";
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Production")));

        // Assert
        Assert.Contains(
            "HTTPS",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WhenUnknownProvider_IsRejected()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:Provider"] = "Unknown";

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Local")));

        // Assert
        Assert.Contains(
            "Disabled",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Gmail",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WhenLocalEnvironment_RejectsPlainHttpForNonLoopbackOrigin()
    {
        // Arrange
        var configuration = CreateGmailConfiguration("http://example.test");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Local")));

        // Assert
        Assert.Contains(
            "localhost",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("https://example.test?query=value")]
    [InlineData("https://example.test#fragment")]
    [InlineData("https://user@example.test")]
    [InlineData("https://example.test/")]
    public void Configure_WhenGmail_RejectsNonCanonicalFrontendOrigins(string origin)
    {
        // Arrange
        var configuration = CreateGmailConfiguration(origin);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Production")));

        // Assert
        Assert.Contains(
            "scheme, host",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WhenGmail_RequiresOAuthSecrets()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:Provider"] = "Gmail";
        configuration["AuthenticationEmail:FrontendOrigin"] = "https://example.test";
        configuration["Gmail:SenderAddress"] = "monkado.app@gmail.com";

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Production")));

        // Assert
        Assert.Contains(
            "ClientId",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WhenGmailSectionIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:Provider"] = "Gmail";
        configuration["AuthenticationEmail:FrontendOrigin"] = "https://example.test";

        // Act
        IServiceCollection action() => new ServiceCollection().ConfigureAuthenticationEmailDelivery(
            configuration,
            new TestHostEnvironment("Production"));

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
    }

    [Theory]
    [InlineData(null, "client-secret", "refresh-token")]
    [InlineData("client-id", null, "refresh-token")]
    [InlineData("client-id", "client-secret", null)]
    public void Configure_WhenGmailOAuthSecretIsMissing_ThrowsInvalidOperationException(
        string? clientId,
        string? clientSecret,
        string? refreshToken)
    {
        // Arrange
        var configuration = CreateGmailConfiguration("https://example.test");
        configuration["Gmail:ClientId"] = clientId;
        configuration["Gmail:ClientSecret"] = clientSecret;
        configuration["Gmail:RefreshToken"] = refreshToken;

        // Act
        IServiceCollection action() => new ServiceCollection().ConfigureAuthenticationEmailDelivery(
            configuration,
            new TestHostEnvironment("Production"));

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
    }

    [Fact]
    public void Configure_WhenGmail_RejectsInvalidSenderAddress()
    {
        // Arrange
        var configuration = CreateGmailConfiguration("https://example.test");
        configuration["Gmail:SenderAddress"] = "invalid address <";

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Production")));

        // Assert
        Assert.Contains(
            "valid e-mail address",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("ftp://example.test")]
    public void Configure_WhenGmailFrontendOriginIsInvalid_ThrowsInvalidOperationException(
        string? origin)
    {
        // Arrange
        var configuration = CreateGmailConfiguration(origin ?? string.Empty);

        // Act
        IServiceCollection action() => new ServiceCollection().ConfigureAuthenticationEmailDelivery(
            configuration,
            new TestHostEnvironment("Local"));

        // Assert
        Assert.Throws<InvalidOperationException>((Func<IServiceCollection>)action);
    }

    [Fact]
    public void Configure_WhenGmailRegistersDeliveryServicesForValidConfiguration_Completes()
    {
        // Arrange
        // Act
        var configuration = CreateGmailConfiguration("https://example.test");
        var services = new ServiceCollection();

        services.ConfigureAuthenticationEmailDelivery(
            configuration,
            new TestHostEnvironment("Production"));

        // Assert
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IGmailApiClient));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IAuthenticationEmailSender));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IAuthenticationEmailDispatcher));
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType == typeof(AuthenticationEmailDeliveryWorker));
    }

    private static ConfigurationManager CreateGmailConfiguration(string frontendOrigin)
    {
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:Provider"] = "Gmail";
        configuration["AuthenticationEmail:FrontendOrigin"] = frontendOrigin;
        configuration["Gmail:SenderAddress"] = "monkado.app@gmail.com";
        configuration["Gmail:ClientId"] = "client-id";
        configuration["Gmail:ClientSecret"] = "client-secret";
        configuration["Gmail:RefreshToken"] = "refresh-token";

        return configuration;
    }

}
