using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

/// <summary>
/// Provides extension methods for configuring services in an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>This class contains methods to simplify the registration and configuration of services using options
/// bound to configuration sections.</remarks>
[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtension
{
    public static IServiceCollection BindAndValidateOptions<T>(this IServiceCollection services,
        IConfiguration configuration)
        where T : class
    {
        return services.AddOptions<T>()
            .Bind(configuration.GetSection(typeof(T).Name))
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .Services;
    }
}
