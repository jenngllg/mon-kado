using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Worker;
using JennGllg.Fr.MonKado.Back.Worker.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public sealed class AuthenticationEmailConfigurationTests
{
    [Fact]
    public void LocalEnvironmentAllowsDisabledDeliveryWithoutGmailSecrets()
    {
        ConfigurationManager configuration = new();
        configuration["AuthenticationEmail:Provider"] = "Disabled";
        ServiceCollection services = new();

        IServiceCollection result = services.AddAuthenticationEmailWorker(
            configuration,
            new TestHostEnvironment("Local"));

        Assert.Same(services, result);
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType == typeof(AuthenticationEmailDeliveryWorker));
    }

    [Fact]
    public void ProductionRejectsDisabledDelivery()
    {
        ConfigurationManager configuration = new();
        configuration["AuthenticationEmail:Provider"] = "Disabled";
        ServiceCollection services = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddAuthenticationEmailWorker(configuration, new TestHostEnvironment("Production")));

        Assert.Contains("cannot be disabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GmailRequiresHttpsFrontendAndAllOAuthSecretsInProduction()
    {
        ConfigurationManager configuration = new();
        configuration["AuthenticationEmail:Provider"] = "Gmail";
        configuration["AuthenticationEmail:FrontendOrigin"] = "http://localhost:5173";
        ServiceCollection services = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddAuthenticationEmailWorker(configuration, new TestHostEnvironment("Production")));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownProviderIsRejected()
    {
        ConfigurationManager configuration = new();
        configuration["AuthenticationEmail:Provider"] = "Unknown";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAuthenticationEmailWorker(
                configuration,
                new TestHostEnvironment("Local")));

        Assert.Contains("Disabled", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Gmail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalEnvironmentRejectsPlainHttpForNonLoopbackOrigin()
    {
        ConfigurationManager configuration = CreateGmailConfiguration("http://example.test");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAuthenticationEmailWorker(
                configuration,
                new TestHostEnvironment("Local")));

        Assert.Contains("localhost", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://example.test/path")]
    [InlineData("https://example.test?query=value")]
    [InlineData("https://example.test#fragment")]
    [InlineData("https://user@example.test")]
    [InlineData("https://example.test/")]
    public void GmailRejectsNonCanonicalFrontendOrigins(string origin)
    {
        ConfigurationManager configuration = CreateGmailConfiguration(origin);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAuthenticationEmailWorker(
                configuration,
                new TestHostEnvironment("Production")));

        Assert.Contains("scheme, host", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GmailRequiresOAuthSecrets()
    {
        ConfigurationManager configuration = new();
        configuration["AuthenticationEmail:Provider"] = "Gmail";
        configuration["AuthenticationEmail:FrontendOrigin"] = "https://example.test";
        configuration["Gmail:SenderAddress"] = "monkado.app@gmail.com";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAuthenticationEmailWorker(
                configuration,
                new TestHostEnvironment("Production")));

        Assert.Contains("ClientId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GmailRejectsInvalidSenderAddress()
    {
        ConfigurationManager configuration = CreateGmailConfiguration("https://example.test");
        configuration["Gmail:SenderAddress"] = "invalid address <";

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAuthenticationEmailWorker(
                configuration,
                new TestHostEnvironment("Production")));

        Assert.Contains("valid e-mail address", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GmailRegistersDeliveryServicesForValidConfiguration()
    {
        ConfigurationManager configuration = CreateGmailConfiguration("https://example.test");
        ServiceCollection services = new();

        services.AddAuthenticationEmailWorker(configuration, new TestHostEnvironment("Production"));

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IGmailApiClient));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAuthenticationEmailSender));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAuthenticationEmailDispatcher));
        Assert.Contains(
            services,
            descriptor => descriptor.ImplementationType == typeof(AuthenticationEmailDeliveryWorker));
    }

    private static ConfigurationManager CreateGmailConfiguration(string frontendOrigin)
    {
        ConfigurationManager configuration = new();
        configuration["AuthenticationEmail:Provider"] = "Gmail";
        configuration["AuthenticationEmail:FrontendOrigin"] = frontendOrigin;
        configuration["Gmail:SenderAddress"] = "monkado.app@gmail.com";
        configuration["Gmail:ClientId"] = "client-id";
        configuration["Gmail:ClientSecret"] = "client-secret";
        configuration["Gmail:RefreshToken"] = "refresh-token";
        return configuration;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Worker.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
