using Testcontainers.PostgreSql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.MigrationTests;

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container
    {
        get;
    } = new PostgreSqlBuilder("postgres:18.4-alpine")
        .WithDatabase("mon_kado_migration_tests")
        .WithUsername("mon_kado")
        .WithPassword("migration-tests-only")
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
