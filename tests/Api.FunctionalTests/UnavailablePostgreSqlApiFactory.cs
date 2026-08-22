using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class UnavailablePostgreSqlApiFactory : WebApplicationFactory<Program>
{
    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=integration-tests-only;" +
        "Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "ConnectionStrings:PostgreSql",
            UnavailableConnectionString);
        builder.UseSetting(
            "Jwt:SigningKey",
            "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=");
    }
}
