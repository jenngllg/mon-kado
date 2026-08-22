using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class PostgreSqlConfigurationTests
{
    [Fact]
    public void Configure_WhenRegistration_DoesNotEnableAutomaticDatabaseRetries()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] =
            "Host=localhost;Database=mon_kado;Username=mon_kado;Password=test";
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureInfrastructureInjection(configuration);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Act
        var executionStrategy = context.Database.CreateExecutionStrategy();

        // Assert
        Assert.False(executionStrategy.RetriesOnFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Configure_WhenRegistration_FailsWhenConnectionStringIsMissingOrBlank(string? connectionString)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] = connectionString;
        var services = new ServiceCollection();

        // Act
        var exception = Assert.Throws<InvalidOperationException>(
            () => services.ConfigureInfrastructureInjection(configuration));

        // Assert
        Assert.Equal(
            "Connection string 'PostgreSql' is required. Configure it with " +
            "'ConnectionStrings:PostgreSql'.",
            exception.Message);
    }
}
