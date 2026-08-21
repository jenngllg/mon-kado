using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MonKadoDataProtectionOptions = JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options.DataProtectionOptions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
/// <summary>
/// Represents data protection configuration.
/// </summary>

public static class DataProtectionConfiguration
{
    private const string ApplicationName = "JennGllg.Fr.MonKado.Back";
    /// <summary>
    /// Executes the configure data protection operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection ConfigureDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(MonKadoDataProtectionOptions.SectionName);
        var options = section.Get<MonKadoDataProtectionOptions>() ?? new MonKadoDataProtectionOptions();

        if (environment.IsProduction() && string.IsNullOrWhiteSpace(options.KeysPath))
            throw new InvalidOperationException("'DataProtection:KeysPath' is required in Production.");

        services.Configure<MonKadoDataProtectionOptions>(section);
        var builder = services
            .AddDataProtection()
            .SetApplicationName(ApplicationName);

        if (!string.IsNullOrWhiteSpace(options.KeysPath))
            builder.PersistKeysToFileSystem(new DirectoryInfo(options.KeysPath));

        services.Configure<IdentityOptions>(identity =>
        {
            identity.Tokens.EmailConfirmationTokenProvider =
                EmailConfirmationTokenProviderOptions.ProviderName;
            identity.Tokens.ProviderMap[EmailConfirmationTokenProviderOptions.ProviderName] =
                new TokenProviderDescriptor(typeof(EmailConfirmationTokenProvider<MonKadoUser>));
        });
        services.Configure<EmailConfirmationTokenProviderOptions>(_ => { });
        services.AddTransient<EmailConfirmationTokenProvider<MonKadoUser>>();

        return services;
    }
}
