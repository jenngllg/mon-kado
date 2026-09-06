using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

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

    /// <summary>Migrates and clears shared application data for image-worker integration tests.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the database reset.</returns>
    public async Task ResetDatabaseAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql(
                Container.GetConnectionString(),
                postgres => postgres.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    "public"))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new MonKadoDbContext(options);
        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users, public.guest_sessions, public.gift_image_deletion_outbox CASCADE;",
            cancellationToken);
    }
}
