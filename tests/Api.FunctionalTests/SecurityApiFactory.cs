using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class SecurityApiFactory(
    string environment = "Local",
    string? allowedOrigin = "http://localhost:5173",
    string? allowedHosts = "localhost",
    string? dataProtectionKeysPath = null,
    string? knownProxyNetwork = "127.0.0.0/8") : WebApplicationFactory<Program>
{
    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=functional-tests-only;" +
        "Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:PostgreSql", UnavailableConnectionString);
        builder.UseSetting("AllowedHosts", allowedHosts);
        builder.UseSetting("WebSecurity:AllowedOrigins:0", allowedOrigin);
        builder.UseSetting("DataProtection:KeysPath", dataProtectionKeysPath);
        builder.UseSetting("ReverseProxy:KnownNetworks:0", knownProxyNetwork);
        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(SecurityTestController).Assembly));
    }
}

public sealed class TemporaryKeyDirectory : IDisposable
{
    public TemporaryKeyDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mon-kado-data-protection-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
