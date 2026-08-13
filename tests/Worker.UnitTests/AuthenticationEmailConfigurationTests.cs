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

        services.AddAuthenticationEmailWorker(configuration, new TestHostEnvironment("Local"));
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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Worker.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
