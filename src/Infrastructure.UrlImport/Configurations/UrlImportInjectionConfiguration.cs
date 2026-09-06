using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Configurations;

/// <summary>Registers isolated merchant import services and a public-only HTTP transport.</summary>
public static class UrlImportInjectionConfiguration
{
    /// <summary>Registers bounded passive imports without cookies, proxies, redirects, or HTTP URL logging.</summary>
    /// <param name="services">The application services.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The configured service collection.</returns>
    public static IServiceCollection ConfigureUrlImportInjection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<UrlImportOptions>, UrlImportOptionsValidator>();
        services
            .AddOptions<UrlImportOptions>()
            .Bind(configuration.GetSection(UrlImportOptions.SectionName))
            .ValidateOnStart();
        services.AddScoped<IImportDnsResolver, ImportDnsResolver>();
        services.AddScoped<IImportSocketConnector, ImportSocketConnector>();
        services.AddScoped<IPublicHttpConnector, PublicHttpConnector>();
        services.AddScoped<IMerchantMetadataExtractor, MerchantMetadataExtractor>();
        services.AddScoped<IWishImportService, WishImportService>();
        services
            .AddHttpClient<IUrlImportClient, UrlImportClient>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MonKado-Import/1.0");
            })
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseProxy = false,
                AutomaticDecompression = DecompressionMethods.All,
                MaxResponseHeadersLength = 16,
                PooledConnectionLifetime = TimeSpan.Zero,
                ConnectCallback = (
                    context,
                    cancellationToken) => provider
                    .GetRequiredService<IPublicHttpConnector>()
                    .ConnectAsync(
                    context.DnsEndPoint.Host,
                    context.DnsEndPoint.Port,
                    cancellationToken)
            });

        return services;
    }
}
