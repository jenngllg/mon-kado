using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishCopyOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenCopyIsDocumented_ExposesAuthenticatedCreationAndSourceIdentifiers()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var operation = json.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/wishlists/{wishlistId}/wishes/copies")
            .GetProperty("post");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(operation.TryGetProperty(
            "security",
            out _));
        var parameters = operation.GetProperty("parameters").EnumerateArray();
        Assert.Contains(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-MonKado-Share-Token");
        var responses = operation.GetProperty("responses");

        foreach (var status in new[]
        {
            "201",
            "400",
            "401",
            "403",
            "404",
            "409",
            "413",
            "415",
            "429",
            "500",
            "503"
        })
        {
            Assert.True(responses.TryGetProperty(
                status,
                out _));
        }

        var headers = responses.GetProperty("201").GetProperty("headers");
        Assert.True(headers.TryGetProperty(
            "ETag",
            out _));
        Assert.True(headers.TryGetProperty(
            "Location",
            out _));
        var schema = json.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("CopyWishRequest");
        Assert.Equal(
            [
                "sourceShareLinkId",
                "sourceWishId"
            ],
            schema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .Order()
                .ToArray());
    }
}
