using Npgsql;
using Testcontainers.PostgreSql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgreSqlTestSuite : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "PostgreSQL";
}

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private const string UnmigratedDatabaseName = "mon_kado_readiness_tests";

    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:18.4-alpine")
        .WithDatabase("mon_kado_tests")
        .WithUsername("mon_kado")
        .WithPassword("integration-tests-only")
        .Build();

    public string UnmigratedConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await Container.StartAsync();

        await using NpgsqlConnection connection = new(Container.GetConnectionString());
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "CREATE DATABASE mon_kado_readiness_tests;";
        await command.ExecuteNonQueryAsync();

        NpgsqlConnectionStringBuilder connectionString = new(Container.GetConnectionString())
        {
            Database = UnmigratedDatabaseName
        };
        UnmigratedConnectionString = connectionString.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync();
    }
}
