using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class ApiBaselineTests(UnavailablePostgreSqlApiFactory factory) : IClassFixture<UnavailablePostgreSqlApiFactory>
{
    private readonly UnavailablePostgreSqlApiFactory _factory = factory;

    [Fact]
    public void GetAsync_WhenApplicationStartsWithoutExternalServices_Completes()
    {
        // Arrange
        // Act
        using var client = _factory.CreateClient();

        // Assert
        Assert.NotNull(client);
        Assert.NotNull(_factory.Server);
    }

    [Fact]
    public async Task GetAsync_WhenLiveness_ReturnsHealthyWhenPostgreSqlIsUnavailable()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await client.GetAsync(
            "/liveness",
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

    [Fact]
    public async Task GetAsync_WhenReadiness_ReturnsServiceUnavailableWhenPostgreSqlIsUnavailable()
    {
        // Arrange
        using var client = _factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await client.GetAsync(
            "/readiness",
            cancellationToken);
        // Act
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            "Unhealthy",
            content);
    }

    [Fact]
    public async Task GetAsync_WhenUnknownRoute_ReturnsNotFound()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(
            "/not-found",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }
}
