using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.IntegrationTests;

public sealed class PostgreSqlConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegistrationFailsWhenConnectionStringIsMissingOrBlank(string? connectionString)
    {
        ConfigurationManager configuration = new();
        configuration["ConnectionStrings:PostgreSql"] = connectionString;
        ServiceCollection services = new();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => services.AddPostgreSqlPersistence(configuration));

        Assert.Equal(
            "Connection string 'PostgreSql' is required. Configure it with " +
            "'ConnectionStrings:PostgreSql'.",
            exception.Message);
    }
}
