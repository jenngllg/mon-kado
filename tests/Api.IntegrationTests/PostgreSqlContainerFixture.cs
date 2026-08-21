using Testcontainers.PostgreSql;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container
    {
        get;
    } = new PostgreSqlBuilder("postgres:18.4-alpine")
        .WithDatabase("mon_kado_api_tests")
        .WithUsername("mon_kado")
        .WithPassword("integration-tests-only")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await Container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
