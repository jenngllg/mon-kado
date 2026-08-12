using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class HealthChecksExtensions
{
    private const string LivenessTag = "live";
    private static readonly TimeSpan PostgreSqlReadinessTimeout = TimeSpan.FromSeconds(2);

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LivenessTag])
            .AddDbContextCheck<MonKadoDbContext>(
                "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                customTestQuery: CanConnectToPostgreSql);

        return services;
    }

    public static IEndpointRouteBuilder MapApiHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/liveness", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LivenessTag)
        });

        endpoints.MapHealthChecks("/readiness", new HealthCheckOptions
        {
            Predicate = _ => true
        });

        return endpoints;
    }

    private static async Task<bool> CanConnectToPostgreSql(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PostgreSqlReadinessTimeout);

        return await context.Database.CanConnectAsync(timeout.Token);
    }
}
