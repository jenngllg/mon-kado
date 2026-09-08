using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Worker.Configurations;

/// <summary>Registers post-erasure delivery without making cleanup depend on Gmail.</summary>
public static class AccountErasureConfiguration
{
    /// <summary>Registers the always-on maintenance worker and optionally the provider dispatcher.</summary>
    /// <param name="services">The host services.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection ConfigureAccountErasure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var email = configuration.GetSection(AuthenticationEmailOptions.SectionName)
            .Get<AuthenticationEmailOptions>() ?? new AuthenticationEmailOptions();

        if (email.IsEnabled)
        {
            services.AddScoped<IAccountErasureEmailSender, GmailAccountErasureEmailSender>();
            services.AddScoped<IAccountErasureEmailDispatcher, AccountErasureEmailDispatcher>();
        }

        services.AddHostedService<AccountErasureWorker>();

        return services;
    }
}
