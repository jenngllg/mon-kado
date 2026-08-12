using System.Net;
using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public sealed class PostgreSqlReadinessTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task ReadinessReturnsHealthyWhenUnmigratedPostgreSqlIsAvailable()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = fixture.Container.GetConnectionString();
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NULL;";
        bool databaseIsUnmigrated = (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("PostgreSQL returned no migration state."));

        Assert.True(databaseIsUnmigrated);

        await using PostgreSqlApiFactory factory = new(connectionString);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/readiness", cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", content);
    }
}
