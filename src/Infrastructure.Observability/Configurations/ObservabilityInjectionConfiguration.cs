using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Logging;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Configurations;

/// <summary>Registers native metrics and optional private snapshot publication.</summary>
public static class ObservabilityInjectionConfiguration
{
    /// <summary>Registers process-local telemetry with a composition-root service identity.</summary>
    /// <param name="services">The service registrations.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <param name="service">The api or worker identity.</param>
    /// <param name="errorCodes">The compiled HTTP error-code allowlist, empty for a non-HTTP host.</param>
    /// <returns>The service registrations.</returns>
    public static IServiceCollection ConfigureObservabilityInjection(
        this IServiceCollection services,
        IConfiguration configuration,
        string service,
        IEnumerable<string> errorCodes)
    {
        var configured = configuration.GetSection("Observability").Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        var options = new ObservabilityOptions
        {
            Enabled = configured.Enabled,
            Directory = configured.Directory,
            Interval = configured.Interval,
            Service = service,
            Version = configured.Version
        };
        var validator = new ObservabilityOptionsValidator();
        var result = validator.Validate(
            null,
            options);

        if (result.Failed)
            throw new OptionsValidationException(
                "Observability",
                typeof(ObservabilityOptions),
                result.Failures);
        services.AddSingleton<IOptions<ObservabilityOptions>>(Microsoft.Extensions.Options.Options.Create(options));
        services.TryAddSingleton(TimeProvider.System);
        // The immutable whitelist is shared by the singleton console formatter.
        services.AddSingleton<ILogPropertyFilter>(new LogPropertyFilter(errorCodes));
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole(console => console.FormatterName = SafeJsonConsoleFormatter.FormatterName)
                .AddConsoleFormatter<SafeJsonConsoleFormatter, ConsoleFormatterOptions>();
        });
        // Counters and boot identity must have exactly one process lifetime across request scopes.
        services.AddSingleton<ApplicationTelemetry>();
        services.AddSingleton<IApplicationTelemetry>(provider => provider.GetRequiredService<ApplicationTelemetry>());
        services.AddSingleton<ITelemetrySnapshotSource>(provider => provider.GetRequiredService<ApplicationTelemetry>());
        // The stateless writer has a single hosted consumer and immutable options.
        services.AddSingleton<ITelemetrySnapshotStore, FileTelemetrySnapshotStore>();
        services.AddHostedService<TelemetrySnapshotWorker>();

        return services;
    }
}
