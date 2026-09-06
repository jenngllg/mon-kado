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

public class WishlistModerationEmailConfigurationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("Disabled", false)]
    [InlineData("Gmail", true)]
    public void ConfigureWishlistModerationEmail_WhenProviderVaries_RegistersOnlyEnabledDelivery(
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
        var result = services.ConfigureWishlistModerationEmail(configuration);

        // Assert
        Assert.Same(
            services,
            result);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(WishlistModerationEmailWorker));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IValidateOptions<WishlistModerationEmailOptions>) && descriptor.ImplementationType == typeof(WishlistModerationEmailOptionsValidator));
        Assert.Equal(
            enabled,
            services.Any(descriptor => descriptor.ServiceType == typeof(IWishlistModerationEmailSender) && descriptor.ImplementationType == typeof(GmailWishlistModerationEmailSender) && descriptor.Lifetime == ServiceLifetime.Scoped));
        Assert.Equal(
            enabled,
            services.Any(descriptor => descriptor.ServiceType == typeof(IWishlistModerationEmailDispatcher) && descriptor.ImplementationType == typeof(WishlistModerationEmailDispatcher) && descriptor.Lifetime == ServiceLifetime.Scoped));
    }
}
