using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Worker.Configurations;
/// <summary>
/// Represents worker injection configuration.
/// </summary>

public static class WorkerInjectionConfiguration
{
    /// <summary>
    /// Executes the configure worker injection operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The operation result.</returns>
    public static IServiceCollection ConfigureWorkerInjection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.ConfigureAuthenticationCleanup(configuration);
        services.ConfigureAuthenticationEmailDelivery(
            configuration,
            environment);
        services.AddHostedService<UnconfirmedAccountCleanupWorker>();
        services.AddHostedService<ExpiredAuthenticationSessionCleanupWorker>();
        services.AddHostedService<ExpiredMemberEmailChangeRequestCleanupWorker>();
        services.AddHostedService<ProcessedAuthenticationEmailCleanupWorker>();
        services.AddHostedService<ExpiredGuestSessionCleanupWorker>();

        return services;
    }
}
