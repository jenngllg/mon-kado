using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public sealed class OpenApiContractTests : IClassFixture<UnavailablePostgreSqlApiFactory>
{
    private readonly UnavailablePostgreSqlApiFactory factory;

    public OpenApiContractTests(UnavailablePostgreSqlApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task GetV1ContractReturnsOpenApi31Document()
    {
        using HttpClient client = factory.CreateClient();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI response body is empty.");
        JsonElement root = document.RootElement;

        Assert.Equal("3.1.1", root.GetProperty("openapi").GetString());
        Assert.Equal("Mon Kado API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("paths").ValueKind);
    }

    [Fact]
    public async Task GetUnknownDocumentReturnsNotFound()
    {
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/openapi/v2.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
