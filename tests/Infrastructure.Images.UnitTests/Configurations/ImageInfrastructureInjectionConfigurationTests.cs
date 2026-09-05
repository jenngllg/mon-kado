using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Configurations;

public class ImageInfrastructureInjectionConfigurationTests
{
    [Fact]
    public void ConfigureImageInfrastructureInjection_WhenConfigurationIsValid_RegistersImageServices()
    {
        // Arrange
        var storagePath = Path.GetFullPath("gift-images");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{GiftImageStorageOptions.SectionName}:StoragePath"] = storagePath
            })
            .Build();
        var services = new ServiceCollection();

        // Act
        var result = services.ConfigureImageInfrastructureInjection(configuration);

        // Assert
        Assert.Same(
            services,
            result);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IValidateOptions<GiftImageStorageOptions>) &&
                descriptor.ImplementationType == typeof(GiftImageStorageOptionsValidator) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IGiftImageProcessor) &&
                descriptor.ImplementationType == typeof(GiftImageProcessor) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IGiftImageStore) &&
                descriptor.ImplementationType == typeof(LocalGiftImageStore) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
