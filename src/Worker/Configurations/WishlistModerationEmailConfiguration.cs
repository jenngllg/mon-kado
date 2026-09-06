using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Configurations;

/// <summary>Wires moderation delivery to the already validated Gmail transport configuration.</summary>
public static class WishlistModerationEmailConfiguration
{
    /// <summary>Registers validated settings, the scoped sender and the dedicated Worker.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection ConfigureWishlistModerationEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<WishlistModerationEmailOptions>, WishlistModerationEmailOptionsValidator>();
        services
            .AddOptions<WishlistModerationEmailOptions>()
            .Bind(configuration.GetSection(WishlistModerationEmailOptions.SectionName))
            .ValidateOnStart();
        var email = configuration
            .GetSection(AuthenticationEmailOptions.SectionName)
            .Get<AuthenticationEmailOptions>() ?? new AuthenticationEmailOptions();

        if (email.IsEnabled)
        {
            services.AddScoped<IWishlistModerationEmailSender, GmailWishlistModerationEmailSender>();
            services.ConfigureWishlistModerationEmailDelivery();
        }

        services.AddHostedService<WishlistModerationEmailWorker>();

        return services;
    }
}
