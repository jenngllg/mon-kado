using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using System.Data.Common;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.MigrationTests;

[Collection(PostgreSqlMigrationTestSuite.Name)]
public class PostgreSqlMigrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task MigrateAsync_WhenMigrations_AreIdempotentAndMatchTheCurrentModel()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        // Act
        var migrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        // Assert
        Assert.Collection(
            migrations,
            migration => Assert.EndsWith(
                "_InitialPersistenceBaseline",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddIdentityAndAccountRegistration",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddEmailConfirmationRequestThrottling",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddAuthenticationEmailDeliveryTracking",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddAuthenticationSessions",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddAuditableUtcDates",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_ReplaceAuthenticationTicketsWithRefreshSessions",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_AddMemberRole",
                migration,
                StringComparison.Ordinal),
            migration => Assert.EndsWith(
                "_UseMemberXminVersion",
                migration,
                StringComparison.Ordinal));
        Assert.False(context.Database.HasPendingModelChanges());

        var tables = await GetPublicTablesAsync(
            context,
            cancellationToken);
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

        var constraints = await GetPublicConstraintsAsync(
            context,
            cancellationToken);
        Assert.Contains(
            "ck_users_display_name_valid",
            constraints);
        Assert.Contains(
            "ck_users_timestamps_consistent",
            constraints);
        Assert.DoesNotContain(
            "ck_users_version_positive",
            constraints);
        Assert.Contains(
            "ck_authentication_email_outbox_kind_valid",
            constraints);
        Assert.Contains(
            "fk_authentication_email_outbox_users_user_id",
            constraints);
        Assert.Contains(
            "ck_authentication_sessions_refresh_token_hash_length",
            constraints);
        Assert.Contains(
            "ck_authentication_sessions_timestamps_consistent",
            constraints);
        Assert.Contains(
            "fk_authentication_sessions_users_user_id",
            constraints);

        var indexes = await GetPublicIndexesAsync(
            context,
            cancellationToken);
        Assert.Contains(
            "ux_users_normalized_email",
            indexes);
        Assert.Contains(
            "ix_users_unconfirmed_account_expiry",
            indexes);
        Assert.Contains(
            "ux_authentication_email_outbox_pending_user_kind",
            indexes);
        Assert.Contains(
            "ix_authentication_email_outbox_pending_delivery",
            indexes);
        Assert.Contains(
            "ix_authentication_email_outbox_user_kind_created_at",
            indexes);
        Assert.Contains(
            "ix_authentication_sessions_expires_at",
            indexes);
        Assert.Contains(
            "ix_authentication_sessions_user_id",
            indexes);

        var columns = await GetAuthenticationEmailOutboxColumnsAsync(
            context,
            cancellationToken);
        Assert.Contains(
            "provider_message_id",
            columns);
        Assert.Equal(
            [
                "created_at",
                "expires_at",
                "id",
                "is_persistent",
                "refresh_token_hash",
                "renewed_at",
                "revoked_at",
                "user_id"
            ],
            await GetAuthenticationSessionColumnsAsync(
                context,
                cancellationToken));
        Assert.True(await IsUserUpdatedAtNullableAsync(
            context,
            cancellationToken));
        Assert.False(await HasUserVersionColumnAsync(
            context,
            cancellationToken));
    }

    [Fact]
    public async Task MigrateAsync_WhenOpaqueSessionExists_DeletesSessionDuringRefreshMigration()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            "20260821115421_AddAuditableUtcDates",
            cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var displayName = "Migration test";
        var email = $"migration-{suffix}@example.test";
        var normalizedEmail = $"MIGRATION-{suffix}@EXAMPLE.TEST";
        var protectedTicket = Convert.FromHexString("01");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.users (
                id,
                display_name,
                created_at,
                version,
                user_name,
                normalized_user_name,
                email,
                normalized_email,
                email_confirmed,
                phone_number_confirmed,
                two_factor_enabled,
                lockout_enabled,
                access_failed_count)
            VALUES (
                {userId},
                {displayName},
                {now},
                {1},
                {email},
                {normalizedEmail},
                {email},
                {normalizedEmail},
                {true},
                {false},
                {false},
                {true},
                {0});
            """,
            cancellationToken);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.authentication_sessions (
                id,
                user_id,
                protected_ticket,
                created_at,
                renewed_at,
                expires_at)
            VALUES (
                {sessionId},
                {userId},
                {protectedTicket},
                {now},
                {now},
                {now.AddHours(8)});
            """,
            cancellationToken);

        // Act
        await context.Database.MigrateAsync(cancellationToken);

        // Assert
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task MigrateAsync_WhenExistingMember_BackfillsAndRemovesMemberRole()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(
            "20260821191432_ReplaceAuthenticationTicketsWithRefreshSessions",
            cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "DELETE FROM public.users;",
            cancellationToken);
        var memberId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.users (
                id,
                display_name,
                created_at,
                version,
                user_name,
                normalized_user_name,
                email,
                normalized_email,
                email_confirmed,
                phone_number_confirmed,
                two_factor_enabled,
                lockout_enabled,
                access_failed_count)
            VALUES (
                {memberId},
                {"Existing member"},
                {now},
                {1},
                {"existing@example.test"},
                {"EXISTING@EXAMPLE.TEST"},
                {"existing@example.test"},
                {"EXISTING@EXAMPLE.TEST"},
                {true},
                {false},
                {false},
                {true},
                {0});
            """,
            cancellationToken);

        // Act
        await context.Database.MigrateAsync(cancellationToken);

        // Assert
        var role = await context.Roles
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == RoleIds.Member,
                cancellationToken);
        Assert.Equal(
            RoleNames.Member,
            role.Name);
        Assert.Equal(
            "MEMBER",
            role.NormalizedName);
        var assignment = await context.UserRoles
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(
            memberId,
            assignment.UserId);
        Assert.Equal(
            RoleIds.Member,
            assignment.RoleId);
        var member = await context.Users
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == memberId,
                cancellationToken);
        Assert.NotEqual(
            0u,
            member.Version);

        await context.Database.MigrateAsync(
            "20260821191432_ReplaceAuthenticationTicketsWithRefreshSessions",
            cancellationToken);
        Assert.Empty(await context.Roles
            .AsNoTracking()
            .Where(value => value.Id == RoleIds.Member)
            .ToArrayAsync(cancellationToken));
        Assert.Empty(await context.UserRoles
            .AsNoTracking()
            .ToArrayAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<string>> GetAuthenticationEmailOutboxColumnsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'authentication_email_outbox'
            ORDER BY column_name;
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<string>> GetAuthenticationSessionColumnsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'authentication_sessions'
            ORDER BY column_name;
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private ServiceProvider CreateServiceProvider()
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();

        var services = new ServiceCollection();
        services.ConfigureInfrastructureInjection(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<IReadOnlyList<string>> GetPublicTablesAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<IReadOnlyList<string>> GetPublicConstraintsAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT constraint_name
            FROM information_schema.table_constraints
            WHERE constraint_schema = 'public'
            ORDER BY constraint_name;
            """;

        var constraints = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            constraints.Add(reader.GetString(0));
        }

        return constraints;
    }

    private static async Task<IReadOnlyList<string>> GetPublicIndexesAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT indexname
            FROM pg_indexes
            WHERE schemaname = 'public'
            ORDER BY indexname;
            """;

        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private static async Task<bool> IsUserUpdatedAtNullableAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT is_nullable = 'YES'
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'users'
              AND column_name = 'updated_at';
            """;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The users.updated_at column is missing."));
    }

    private static async Task<bool> HasUserVersionColumnAsync(
        MonKadoDbContext context,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'users'
                  AND column_name = 'version');
            """;

        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("The users table could not be inspected."));
    }
}
