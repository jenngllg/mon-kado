using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Tests.Common;

public static class TelemetryTestServices
{
    public static IServiceCollection AddTestTelemetry(this IServiceCollection services)
    {
        services.TryAddSingleton<IApplicationTelemetry>(_ => new ApplicationTelemetry(
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions { Service = "worker" })));

        return services;
    }
}
