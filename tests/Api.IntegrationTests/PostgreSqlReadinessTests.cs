using Npgsql;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class PostgreSqlReadinessTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetAsync_WhenReadiness_ReturnsHealthyWhenPostgreSqlIsAvailable()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = fixture.Container.GetConnectionString();

        await using (NpgsqlConnection connection = new(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var factory = new PostgreSqlApiFactory(connectionString);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/readiness",
            cancellationToken);
        // Act
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "Healthy",
            content);
    }
}
