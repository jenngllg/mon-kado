using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class StaticOpenApiFactory(string documentPath) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(
            "OpenApi:DocumentPath",
            documentPath);
        builder.UseSetting(
            "ConnectionStrings:PostgreSql",
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test-only;Timeout=1;Pooling=false");
        builder.UseSetting(
            "Jwt:SigningKey",
            "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=");
        builder.UseSetting(
            "WebSecurity:AllowedOrigins:0",
            "https://static-openapi.invalid");
        builder.UseSetting(
            "GoogleAuthentication:Enabled",
            "false");
    }
}
