using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Worker.Configurations;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Configurations;

public class AccountErasureConfigurationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("Disabled", false)]
    [InlineData("Gmail", true)]
    public void ConfigureAccountErasure_WhenProviderVaries_RegistersOnlyEnabledDelivery(
        string? provider,
        bool enabled)
    {
        // Arrange
        var values = new Dictionary<string, string?>();

        if (provider is not null)
            values["AuthenticationEmail:Provider"] = provider;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();

        // Act
        var result = services.ConfigureAccountErasure(configuration);

        // Assert
        Assert.Same(
            services,
            result);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(AccountErasureWorker));
        Assert.Equal(
            enabled,
            services.Any(descriptor => descriptor.ServiceType == typeof(IAccountErasureEmailSender) && descriptor.ImplementationType == typeof(GmailAccountErasureEmailSender) && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.Equal(
            enabled,
            services.Any(descriptor => descriptor.ServiceType == typeof(IAccountErasureEmailDispatcher) && descriptor.ImplementationType == typeof(AccountErasureEmailDispatcher) && descriptor.Lifetime == ServiceLifetime.Scoped));
    }
}
