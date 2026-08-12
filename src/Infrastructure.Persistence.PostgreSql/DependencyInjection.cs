using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;

public static class DependencyInjection
{
    private const string ConnectionStringName = "PostgreSql";

    public static IServiceCollection AddPostgreSqlPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is required. " +
                $"Configure it with 'ConnectionStrings:{ConnectionStringName}'.");
        }

        services.AddDbContextPool<MonKadoDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(MonKadoDbContext).Assembly.FullName);
                    npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "public");
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 1,
                        maxRetryDelay: TimeSpan.FromMilliseconds(500),
                        errorCodesToAdd: null);
                })
                .UseSnakeCaseNamingConvention());

        return services;
    }
}
