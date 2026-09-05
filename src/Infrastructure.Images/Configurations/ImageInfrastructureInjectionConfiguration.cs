using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.Configurations;

/// <summary>
/// Configures image processing and durable local storage dependencies.
/// </summary>
public static class ImageInfrastructureInjectionConfiguration
{
    /// <summary>
    /// Registers and validates the gift-image infrastructure.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection ConfigureImageInfrastructureInjection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<GiftImageStorageOptions>, GiftImageStorageOptionsValidator>();
        services.AddOptions<GiftImageStorageOptions>()
            .Bind(configuration.GetSection(GiftImageStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IGiftImageProcessor, GiftImageProcessor>();
        services.AddSingleton<IGiftImageStore, LocalGiftImageStore>();

        return services;
    }
}
