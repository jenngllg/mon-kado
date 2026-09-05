using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

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
        await Container.StartAsync(TestContext.Current.CancellationToken);
        await using var context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Container.DisposeAsync();
    }

    /// <summary>
    /// Resets all application data between API integration tests.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users, public.guest_sessions, public.gift_image_deletion_outbox CASCADE;",
            cancellationToken);
    }

    private MonKadoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql(
                Container.GetConnectionString(),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(MonKadoDbContext).Assembly.FullName);
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        "public");
                })
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MonKadoDbContext(options);
    }
}
