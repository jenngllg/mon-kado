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
        Assert.Collection(
            migrations,
            migration => Assert.EndsWith("_InitialPersistenceBaseline", migration, StringComparison.Ordinal),
            migration => Assert.EndsWith("_AddIdentityAndAccountRegistration", migration, StringComparison.Ordinal),
            migration => Assert.EndsWith("_AddEmailConfirmationRequestThrottling", migration, StringComparison.Ordinal),
            migration => Assert.EndsWith("_AddAuthenticationEmailDeliveryTracking", migration, StringComparison.Ordinal),
            migration => Assert.EndsWith("_AddAuthenticationSessions", migration, StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());

        IReadOnlyList<string> tables = await GetPublicTables(context, cancellationToken);
        Assert.Equal(
            [
                "__EFMigrationsHistory",
                "authentication_email_outbox",
                "authentication_sessions",
                "role_claims",
                "roles",
                "user_claims",
                "user_logins",
                "user_roles",
                "user_tokens",
                "users"
            ],
            tables);

        IReadOnlyList<string> constraints = await GetPublicConstraints(context, cancellationToken);
        Assert.Contains("ck_users_display_name_valid", constraints);
        Assert.Contains("ck_users_timestamps_consistent", constraints);
        Assert.Contains("ck_authentication_email_outbox_kind_valid", constraints);
        Assert.Contains("fk_authentication_email_outbox_users_user_id", constraints);
        Assert.Contains("ck_authentication_sessions_ticket_not_empty", constraints);
        Assert.Contains("ck_authentication_sessions_timestamps_consistent", constraints);
        Assert.Contains("fk_authentication_sessions_users_user_id", constraints);

        IReadOnlyList<string> indexes = await GetPublicIndexes(context, cancellationToken);
        Assert.Contains("ux_users_normalized_email", indexes);
        Assert.Contains("ix_users_unconfirmed_account_expiry", indexes);
        Assert.Contains("ux_authentication_email_outbox_pending_user_kind", indexes);
        Assert.Contains("ix_authentication_email_outbox_pending_delivery", indexes);
        Assert.Contains("ix_authentication_email_outbox_user_kind_created_at", indexes);
        Assert.Contains("ix_authentication_sessions_expires_at", indexes);
        Assert.Contains("ix_authentication_sessions_user_id", indexes);

        IReadOnlyList<string> columns = await GetAuthenticationEmailOutboxColumns(context, cancellationToken);
        Assert.Contains("provider_message_id", columns);
    }

    private static async Task<IReadOnlyList<string>> GetAuthenticationEmailOutboxColumns(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'authentication_email_outbox'
            ORDER BY column_name;
            """;
        List<string> columns = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
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

    private static async Task<IReadOnlyList<string>> GetPublicConstraints(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT constraint_name
            FROM information_schema.table_constraints
            WHERE constraint_schema = 'public'
            ORDER BY constraint_name;
            """;

        List<string> constraints = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            constraints.Add(reader.GetString(0));
        }

        return constraints;
    }

    private static async Task<IReadOnlyList<string>> GetPublicIndexes(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        DbConnection connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using DbCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
            ORDER BY indexname;
            """;

        List<string> indexes = [];
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }
}
