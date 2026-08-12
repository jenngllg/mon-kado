using System.Data.Common;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.MigrationTests;

[Collection(PostgreSqlMigrationTestSuite.Name)]
public sealed class PostgreSqlMigrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task MigrationsAreIdempotentAndMatchTheCurrentModel()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider provider = CreateServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        IEnumerable<string> migrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        string migration = Assert.Single(migrations);

        Assert.EndsWith("_InitialPersistenceBaseline", migration, StringComparison.Ordinal);
        Assert.False(context.Database.HasPendingModelChanges());

        IReadOnlyList<string> tables = await GetPublicTables(context, cancellationToken);
        Assert.Equal(["__EFMigrationsHistory"], tables);
    }

    private ServiceProvider CreateServiceProvider()
    {
        ConfigurationManager configuration = new();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();

        ServiceCollection services = new();
        services.AddPostgreSqlPersistence(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<IReadOnlyList<string>> GetPublicTables(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        List<string> tables = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
