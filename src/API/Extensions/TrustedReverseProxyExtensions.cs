using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class TrustedReverseProxyExtensions
{
    private const string KnownNetworksConfigurationPath = "ReverseProxy:KnownNetworks";

    public static IServiceCollection AddTrustedReverseProxy(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        string[] configuredNetworks =
            configuration.GetSection(KnownNetworksConfigurationPath).Get<string[]>() ?? [];
        HashSet<IPNetwork> knownNetworks = [];
        foreach (string configuredNetwork in configuredNetworks)
        {
            if (!IPNetwork.TryParse(configuredNetwork, out IPNetwork network) ||
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

            foreach (IPNetwork knownNetwork in knownNetworks)
            {
                options.KnownIPNetworks.Add(knownNetwork);
            }
        });

        return services;
    }

    public static WebApplication UseTrustedReverseProxy(this WebApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.UseForwardedHeaders();
        return application;
    }
}
