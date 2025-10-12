using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;
using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

[ExcludeFromCodeCoverage]
public static class ConfigureInjectionExtensions
{
    /// <summary>
    /// Configures the dependency injection for persistence-related services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the persistence services will be added.</param>
    public static void ConfigurePersistenceInjection(this IServiceCollection services)
    {
        ConfigurePostgreSql(services);
        RegisterPostgreSqlHealthChecks(services);
    }

    /// <summary>
    /// Configures PostgreSQL-related services for dependency injection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the PostgreSQL services will be added.</param>
    private static void ConfigurePostgreSql(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IOptions<PostgreSqlConfiguration>>().Value;
            ArgumentException.ThrowIfNullOrWhiteSpace(configuration.ConnectionString);

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(configuration.ConnectionString);
            return dataSourceBuilder.Build();
        });

        services.AddScoped<IDbConnection>(sp =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            return dataSource.CreateConnection();
        });
    }

    /// <summary>
    /// Registers PostgreSQL health checks for the application.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to which the health checks are added.</param>
    private static void RegisterPostgreSqlHealthChecks(IServiceCollection services)
    {
        services.AddHealthChecks().AddNpgSql(sp =>
        {
            var configuration = sp.GetRequiredService<IOptions<PostgreSqlConfiguration>>().Value;
            ArgumentException.ThrowIfNullOrWhiteSpace(configuration.ConnectionString);

            return configuration.ConnectionString;
        },
        tags: ["services"]);
    }
}
