using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class HealthChecksExtensions
{
    private const string LivenessTag = "live";

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LivenessTag]);

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
}
