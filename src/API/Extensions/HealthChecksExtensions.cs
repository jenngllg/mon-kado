using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents health checks extensions.
/// </summary>

public static class HealthChecksExtensions
{
    private const string LivenessTag = "live";
    private static readonly TimeSpan _postgreSqlReadinessTimeout = TimeSpan.FromSeconds(2);
    /// <summary>
    /// Executes the add api health checks operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy(),
                [LivenessTag])
            .AddDbContextCheck<MonKadoDbContext>(
                "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                customTestQuery: CanConnectToPostgreSqlAsync);

        return services;
    }
    /// <summary>
    /// Executes the map api health checks operation.
    /// </summary>
    /// <param name="endpoints">The endpoints.</param>
    /// <returns>The operation result.</returns>

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(
            "/liveness",
            new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(LivenessTag)
            });

        endpoints.MapHealthChecks(
            "/readiness",
            new HealthCheckOptions
            {
                Predicate = _ => true
            });

        return endpoints;
    }

    private static async Task<bool> CanConnectToPostgreSqlAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_postgreSqlReadinessTimeout);

        return await context.Database.CanConnectAsync(timeout.Token);
    }
}
