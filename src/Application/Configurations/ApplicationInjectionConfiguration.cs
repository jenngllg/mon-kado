using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Application.Configurations;
/// <summary>
/// Represents application injection configuration.
/// </summary>

public static class ApplicationInjectionConfiguration
{
    /// <summary>
    /// Executes the configure application injection operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The operation result.</returns>
    public static IServiceCollection ConfigureApplicationInjection(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
            configuration.RegisterServicesFromAssembly(typeof(ApplicationInjectionConfiguration).Assembly));
        services.AddValidatorsFromAssemblyContaining(typeof(ApplicationInjectionConfiguration));
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(ValidationBehavior<,>));

        return services;
    }
}
