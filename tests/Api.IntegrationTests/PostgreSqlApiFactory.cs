using JennGllg.Fr.MonKado.Back.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public sealed class PostgreSqlApiFactory(
    string connectionString,
    TimeProvider? timeProvider = null,
    TimeSpan? emailConfirmationTokenLifespan = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:PostgreSql", connectionString);
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("WebSecurity:AllowedOrigins:0", "http://localhost:5173");
        builder.ConfigureTestServices(services =>
        {
            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }

            if (emailConfirmationTokenLifespan is { } lifespan)
            {
                services.Configure<EmailConfirmationTokenProviderOptions>(
                    options => options.TokenLifespan = lifespan);
            }
        });
    }
}
