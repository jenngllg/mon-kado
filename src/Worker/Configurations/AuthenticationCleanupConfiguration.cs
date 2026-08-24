using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Worker.Configurations;

/// <summary>
/// Configures the shared authentication cleanup schedule.
/// </summary>
public static class AuthenticationCleanupConfiguration
{
    /// <summary>
    /// Registers and validates the authentication cleanup options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection ConfigureAuthenticationCleanup(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(AuthenticationCleanupOptions.SectionName);
        var options = section.Get<AuthenticationCleanupOptions>() ??
            new AuthenticationCleanupOptions();

        if (options.BatchSize is < 1 or > 10_000)
        {
            throw new InvalidOperationException(
                "'AuthenticationCleanup:BatchSize' must be between 1 and 10000.");
        }

        if (options.Interval <= TimeSpan.Zero ||
            options.Interval > TimeSpan.FromDays(7))
        {
            throw new InvalidOperationException(
                "'AuthenticationCleanup:Interval' must be greater than zero and at most 7 days.");
        }

        if (options.FailureRetryInterval <= TimeSpan.Zero ||
            options.FailureRetryInterval > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException(
                "'AuthenticationCleanup:FailureRetryInterval' must be greater than zero and at most 1 day.");
        }

        services.Configure<AuthenticationCleanupOptions>(section);

        return services;
    }
}
