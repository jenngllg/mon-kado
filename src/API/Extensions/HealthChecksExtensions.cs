using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Configurations;

/// <summary>
/// Provides extension methods for configuring application healthchecks
/// </summary>
[ExcludeFromCodeCoverage]
public static class HealthChecksExtensions
{
    /// <summary>
    /// Configures health checks for the application.
    /// </summary>
    /// <remarks>This method adds a basic liveness health check to the application's health check pipeline.
    /// The liveness check always returns a healthy status.</remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the health checks are added.</param>
    public static void ConfigureHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("liveness", () => HealthCheckResult.Healthy());
    }

    /// <summary>
    /// Configures the application to use custom health check endpoints for liveness and readiness probes.
    /// </summary>
    /// <remarks>This method sets up two health check endpoints: <list type="bullet"> <item> <description>
    /// <c>/liveness</c>: Monitors the application's liveness by checking health checks with the name "liveness".
    /// </description> </item> <item> <description> <c>/readiness</c>: Monitors the application's readiness by checking
    /// health checks tagged with "services" or "external". </description> </item> </list> Both endpoints return a
    /// JSON-formatted response using the <see cref="UIResponseWriter.WriteHealthCheckUIResponse"/> writer.</remarks>
    /// <param name="app">The <see cref="IApplicationBuilder"/> instance used to configure the application's request pipeline.</param>
    public static void UseCustomHealthChecks(this IApplicationBuilder app)
    {
        app.UseHealthChecks("/liveness", new HealthCheckOptions
        {
            Predicate = check => check.Name == "liveness",
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        app.UseHealthChecks("/readiness", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("services") || check.Tags.Contains("external"),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });
    }
}
