using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class PostgreSqlConfigurationTests
{
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
