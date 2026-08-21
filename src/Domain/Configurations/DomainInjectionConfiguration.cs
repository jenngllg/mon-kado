using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Domain.Configurations;

/// <summary>
/// Provides dependency-injection registrations owned by the Domain project.
/// </summary>
public static class DomainInjectionConfiguration
{
    /// <summary>
    /// Registers Domain services in the supplied service collection.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection ConfigureDomainInjection(this IServiceCollection services)
    {

        return services;
    }
}
