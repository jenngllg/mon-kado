using Microsoft.AspNetCore.HttpOverrides;

using IPNetwork = System.Net.IPNetwork;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents trusted reverse proxy extensions.
/// </summary>

public static class TrustedReverseProxyExtensions
{
    private const string KnownNetworksConfigurationPath = "ReverseProxy:KnownNetworks";
    /// <summary>
    /// Executes the add trusted reverse proxy operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection AddTrustedReverseProxy(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredNetworks =
            configuration.GetSection(KnownNetworksConfigurationPath).Get<string[]>() ?? [];
        var knownNetworks = new HashSet<IPNetwork>();
        foreach (var configuredNetwork in configuredNetworks)
        {

            if (!IPNetwork.TryParse(
                configuredNetwork,
                out var network) ||
                network.PrefixLength == 0)
            {

                throw new InvalidOperationException(
                    $"'{configuredNetwork}' is not a restricted CIDR network. " +
                    $"Configure '{KnownNetworksConfigurationPath}' with the dedicated proxy network.");
            }

            knownNetworks.Add(network);
        }

        if (environment.IsProduction() && knownNetworks.Count == 0)
        {

            throw new InvalidOperationException(
                $"At least one trusted proxy network is required in Production. " +
                $"Configure '{KnownNetworksConfigurationPath}'.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var knownNetwork in knownNetworks)
            {
                options.KnownIPNetworks.Add(knownNetwork);
            }
        });

        return services;
    }
    /// <summary>
    /// Executes the use trusted reverse proxy operation.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns>The operation result.</returns>

    public static WebApplication UseTrustedReverseProxy(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.UseForwardedHeaders();

        return application;
    }
}
