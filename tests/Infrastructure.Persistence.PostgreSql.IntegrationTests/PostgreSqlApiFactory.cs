using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.IntegrationTests;

public sealed class PostgreSqlApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:PostgreSql", connectionString);
    }
}
