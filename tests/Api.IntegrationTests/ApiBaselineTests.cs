using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public sealed class ApiBaselineTests : IClassFixture<UnavailablePostgreSqlApiFactory>
{
    private readonly UnavailablePostgreSqlApiFactory factory;

    public ApiBaselineTests(UnavailablePostgreSqlApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void ApplicationStartsWithoutExternalServices()
    {
        using HttpClient client = factory.CreateClient();

        Assert.NotNull(client);
        Assert.NotNull(factory.Server);
    }

    [Fact]
    public async Task LivenessReturnsHealthyWhenPostgreSqlIsUnavailable()
    {
        using HttpClient client = factory.CreateClient();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        HttpResponseMessage response = await client.GetAsync("/liveness", cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", content);
    }

    [Fact]
    public async Task ReadinessReturnsServiceUnavailableWhenPostgreSqlIsUnavailable()
    {
        using HttpClient client = factory.CreateClient();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        HttpResponseMessage response = await client.GetAsync("/readiness", cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", content);
    }

    [Fact]
    public async Task UnknownRouteReturnsNotFound()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/not-found",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
