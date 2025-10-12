using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.Mock.Configurations;

[ExcludeFromCodeCoverage]
public static class ConfigureInjectionExtensions
{
    /// <summary>
    /// Configures mocked persistence services for dependency injection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the mocked persistence services will be added.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance, allowing for method chaining.</returns>
    public static IServiceCollection ConfigureMockedPersistenceInjection(this IServiceCollection services)
    {
        return services;
    }
}
