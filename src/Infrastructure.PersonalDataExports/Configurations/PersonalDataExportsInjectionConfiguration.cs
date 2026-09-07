using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Configurations;

/// <summary>Registers private archive storage and shared export limits.</summary>
public static class PersonalDataExportsInjectionConfiguration
{
    /// <summary>Registers storage and verifies its private volume before host startup.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection ConfigurePersonalDataExportsInjection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<PersonalDataExportOptions>, PersonalDataExportOptionsValidator>();
        services.AddSingleton<IValidateOptions<PersonalDataExportStorageOptions>, PersonalDataExportStorageOptionsValidator>();
        services
            .AddOptions<PersonalDataExportOptions>()
            .Bind(configuration.GetSection(PersonalDataExportOptions.SectionName))
            .ValidateOnStart();
        services
            .AddOptions<PersonalDataExportStorageOptions>()
            .Bind(configuration.GetSection(PersonalDataExportOptions.SectionName))
            .ValidateOnStart();
        // The store shares an atomic best-effort scan cursor; filesystem safety uses process-independent locks.
        services.AddSingleton<IPersonalDataExportStore, LocalPersonalDataExportStore>();
        services.AddScoped<IPersonalDataExportArchiveBuilder, PersonalDataExportArchiveBuilder>();
        services.AddHostedService<PersonalDataExportStorageInitializer>();

        return services;
    }
}
