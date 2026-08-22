using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class SecurityApiFactory(
    string environment = "Local",
    string? allowedOrigin = "http://localhost:5173",
    string? allowedHosts = "localhost",
    string? dataProtectionKeysPath = null,
    string? knownProxyNetwork = "127.0.0.0/8",
    IPAddress? remoteIpAddress = null) : WebApplicationFactory<Program>
{
    public const string JwtAudience = "MonKado.Frontend";
    public const string JwtIssuer = "MonKado.Api";
    public const string JwtSigningKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=functional-tests-only;" +
        "Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting(
            "ConnectionStrings:PostgreSql",
            UnavailableConnectionString);
        builder.UseSetting(
            "AllowedHosts",
            allowedHosts);
        builder.UseSetting(
            "WebSecurity:AllowedOrigins:0",
            allowedOrigin);
        builder.UseSetting(
            "DataProtection:KeysPath",
            dataProtectionKeysPath);
        builder.UseSetting(
            "Jwt:SigningKey",
            JwtSigningKey);
        builder.UseSetting(
            "ReverseProxy:KnownNetworks:0",
            knownProxyNetwork);
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(SecurityTestController).Assembly);

            if (remoteIpAddress is not null)
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(remoteIpAddress));
        });
    }
}
