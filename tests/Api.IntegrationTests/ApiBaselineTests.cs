using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public sealed class ApiBaselineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ApiBaselineTests(WebApplicationFactory<Program> factory)
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

    [Theory]
    [InlineData("/liveness")]
    [InlineData("/readiness")]
    public async Task HealthCheckReturnsHealthy(string path)
    {
        using HttpClient client = factory.CreateClient();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        HttpResponseMessage response = await client.GetAsync(path, cancellationToken);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", content);
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
