using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Worker.Configurations;

/// <summary>
/// Configures obsolete and pending gift-image cleanup.
/// </summary>
public static class GiftImageCleanupConfiguration
{
    /// <summary>
    /// Registers and validates gift-image cleanup settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection ConfigureGiftImageCleanup(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(GiftImageCleanupOptions.SectionName);
        var options = section.Get<GiftImageCleanupOptions>() ?? new GiftImageCleanupOptions();

        if (options.BatchSize is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "'GiftImageCleanup:BatchSize' must be between 1 and 1000.");
        }

        if (options.Interval <= TimeSpan.Zero ||
            options.FailureRetryInterval <= TimeSpan.Zero ||
            options.LeaseDuration <= TimeSpan.Zero ||
            options.PendingGracePeriod < TimeSpan.FromMinutes(5) ||
            options.MaximumRetryDelay < TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                "Gift-image cleanup durations are invalid.");
        }

        services.Configure<GiftImageCleanupOptions>(section);

        return services;
    }
}
