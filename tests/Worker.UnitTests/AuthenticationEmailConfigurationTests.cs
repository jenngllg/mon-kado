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
using Microsoft.Extensions.Options;

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
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType ==
                typeof(ProcessedAuthenticationEmailCleanupWorker));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Configure_WhenProcessedRetentionIsOutsideAllowedRange_IsRejected(
        int retentionDays)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:ProcessedRetentionDays"] = retentionDays.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Local")));

        // Assert
        Assert.Contains(
            "between 1 and 365",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void Configure_WhenProcessedRetentionIsAtAllowedBoundary_BindsValue(
        int retentionDays)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationEmail:ProcessedRetentionDays"] = retentionDays.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var services = new ServiceCollection();
        services.ConfigureAuthenticationEmailDelivery(
            configuration,
            new TestHostEnvironment("Local"));
        using var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<AuthenticationEmailOptions>>();

        // Assert
        Assert.Equal(
            retentionDays,
            options.Value.ProcessedRetentionDays);
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

    [Theory]
    [InlineData("AuthenticationEmail:DeliveryBatchSize", "0")]
    [InlineData("AuthenticationEmail:DeliveryBatchSize", "1001")]
    [InlineData("AuthenticationEmail:MaximumDeliveryAttempts", "0")]
    [InlineData("AuthenticationEmail:MaximumDeliveryAttempts", "101")]
    public void Configure_WhenDeliveryIntegerIsOutsideAllowedRange_IsRejected(
        string configurationKey,
        string value)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration[configurationKey] = value;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Local")));

        // Assert
        Assert.Contains(
            "must be between",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AuthenticationEmail:DeliveryLeaseDuration", "00:00:00")]
    [InlineData("AuthenticationEmail:PollInterval", "01:00:01")]
    [InlineData("AuthenticationEmail:FailureRetryInterval", "00:00:00")]
    [InlineData("AuthenticationEmail:FirstRetryDelay", "00:00:00")]
    [InlineData("AuthenticationEmail:MaximumRetryDelay", "8.00:00:00")]
    public void Configure_WhenDeliveryDurationIsOutsideAllowedRange_IsRejected(
        string configurationKey,
        string value)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration[configurationKey] = value;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Local")));

        // Assert
        Assert.Contains(
            "must be between",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AuthenticationEmail:FirstRetryDelay", "00:06:00")]
    [InlineData("AuthenticationEmail:SecondRetryDelay", "00:16:00")]
    [InlineData("AuthenticationEmail:ThirdRetryDelay", "01:01:00")]
    [InlineData("AuthenticationEmail:FourthRetryDelay", "07:00:00")]
    public void Configure_WhenTransientRetryDelaysAreNotOrdered_IsRejected(
        string configurationKey,
        string value)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration[configurationKey] = value;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Local")));

        // Assert
        Assert.Contains(
            "ordered",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AuthenticationEmail:SubsequentRetryDelay", "1.01:00:00")]
    [InlineData("AuthenticationEmail:SlowRetryDelay", "1.01:00:00")]
    public void Configure_WhenMaximumRetryDelayDoesNotCoverConfiguredDelay_IsRejected(
        string configurationKey,
        string value)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration[configurationKey] = value;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Local")));

        // Assert
        Assert.Contains(
            "MaximumRetryDelay",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:05:01")]
    public void Configure_WhenGmailRequestTimeoutIsOutsideAllowedRange_IsRejected(string timeout)
    {
        // Arrange
        var configuration = CreateGmailConfiguration("https://example.test");
        configuration["Gmail:RequestTimeout"] = timeout;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationEmailDelivery(
                configuration,
                new TestHostEnvironment("Production")));

        // Assert
        Assert.Contains(
            "Gmail:RequestTimeout",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureAuthenticationCleanup_WhenConfigurationIsValid_BindsOptions()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["AuthenticationCleanup:BatchSize"] = "42";
        configuration["AuthenticationCleanup:Interval"] = "02:00:00";
        configuration["AuthenticationCleanup:FailureRetryInterval"] = "00:03:00";
        var services = new ServiceCollection();

        // Act
        var result = services.ConfigureAuthenticationCleanup(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthenticationCleanupOptions>>();

        // Assert
        Assert.Same(
            services,
            result);
        Assert.Equal(
            42,
            options.Value.BatchSize);
        Assert.Equal(
            TimeSpan.FromHours(2),
            options.Value.Interval);
        Assert.Equal(
            TimeSpan.FromMinutes(3),
            options.Value.FailureRetryInterval);
    }

    [Theory]
    [InlineData("AuthenticationCleanup:BatchSize", "0")]
    [InlineData("AuthenticationCleanup:BatchSize", "10001")]
    [InlineData("AuthenticationCleanup:Interval", "00:00:00")]
    [InlineData("AuthenticationCleanup:Interval", "8.00:00:00")]
    [InlineData("AuthenticationCleanup:FailureRetryInterval", "00:00:00")]
    [InlineData("AuthenticationCleanup:FailureRetryInterval", "2.00:00:00")]
    public void ConfigureAuthenticationCleanup_WhenValueIsOutsideAllowedRange_IsRejected(
        string configurationKey,
        string value)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration[configurationKey] = value;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().ConfigureAuthenticationCleanup(configuration));

        // Assert
        Assert.Contains(
            "AuthenticationCleanup",
            exception.Message,
            StringComparison.Ordinal);
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
