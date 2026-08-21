using Testcontainers.PostgreSql;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

public sealed class PostgreSqlWorkerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container
    {
        get;
    } = new PostgreSqlBuilder("postgres:18.4-alpine")
        .WithDatabase("mon_kado_worker_tests")
        .WithUsername("mon_kado")
        .WithPassword("worker-tests-only")
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
