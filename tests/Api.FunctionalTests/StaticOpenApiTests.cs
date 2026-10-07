using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class StaticOpenApiTests
{
    private const string Document = "{\"openapi\":\"3.1.1\",\"info\":{\"title\":\"Mon Kado API\",\"version\":\"v1\"},\"paths\":{}}";

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task GetAsync_WhenBuildArtifactIsConfigured_ReturnsExactArtifactWithSecurityHeadersAsync(string method)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var documentPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                documentPath,
                Document,
                cancellationToken);
            await using var factory = new StaticOpenApiFactory(documentPath);
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(
                new HttpMethod(method),
                "/openapi/v1.json");
            request.Headers.Add(
                "Origin",
                "https://static-openapi.invalid");

            // Act
            using var response = await client.SendAsync(
                request,
                cancellationToken);

            // Assert
            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);
            Assert.Equal(
                "application/json",
                response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                "nosniff",
                Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            Assert.Equal(
                "DENY",
                Assert.Single(response.Headers.GetValues("X-Frame-Options")));
            Assert.Equal(
                "https://static-openapi.invalid",
                Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Assert.Equal(
                method == "HEAD" ? string.Empty : Document,
                body);
        }
        finally
        {
            File.Delete(documentPath);
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("v2")]
    [InlineData("V1")]
    public async Task GetAsync_WhenDocumentNameIsNotPublished_ReturnsNotFoundAsync(string documentName)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var documentPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(
                documentPath,
                Document,
                cancellationToken);
            await using var factory = new StaticOpenApiFactory(documentPath);
            using var client = factory.CreateClient();

            // Act
            using var response = await client.GetAsync(
                $"/openapi/{documentName}.json",
                cancellationToken);

            // Assert
            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
            Assert.DoesNotContain(
                Document,
                await response.Content.ReadAsStringAsync(cancellationToken));
        }
        finally
        {
            File.Delete(documentPath);
        }
    }
}
