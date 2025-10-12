using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Configurations;

public class ConfigureInjectionExtensionsTests
{
    [Fact]
    public void ConfigurePersistenceInjection_RegistersPostgreSqlServices()
    {
        var services = new ServiceCollection();
        services.Configure<PostgreSqlConfiguration>(options =>
        {
            options.ConnectionString = "Host=localhost;Port=5432;Database=mon_kado;Username=user;Password=pass";
        });

        services.ConfigurePersistenceInjection();

        using var serviceProvider = services.BuildServiceProvider();

        var dataSource = serviceProvider.GetService<NpgsqlDataSource>();
        var connection = serviceProvider.GetService<IDbConnection>();

        Assert.NotNull(dataSource);
        Assert.NotNull(connection);
        Assert.IsType<NpgsqlConnection>(connection);
    }
}
