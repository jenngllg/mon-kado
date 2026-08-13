using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

public static class DataProtectionExtensions
{
    private const string ApplicationName = "JennGllg.Fr.MonKado.Back";

    public static IServiceCollection AddMonKadoDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        IConfigurationSection section = configuration.GetSection(DataProtectionOptions.SectionName);
        DataProtectionOptions options = section.Get<DataProtectionOptions>() ?? new DataProtectionOptions();
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(options.KeysPath))
        {
            throw new InvalidOperationException("'DataProtection:KeysPath' is required in Production.");
        }

        services.Configure<DataProtectionOptions>(section);
        IDataProtectionBuilder builder = services
            .AddDataProtection()
            .SetApplicationName(ApplicationName);
        if (!string.IsNullOrWhiteSpace(options.KeysPath))
        {
            builder.PersistKeysToFileSystem(new DirectoryInfo(options.KeysPath));
        }

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
