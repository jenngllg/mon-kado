using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistSharePreviewOpenApiTests
{
    [Fact]
    public async Task GetAsync_WhenDatabaseUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/shared-wishlists/{Guid.CreateVersion7()}/preview",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            System.Net.HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
    }

    [Theory]
    [InlineData("", "text/html")]
    [InlineData("/image", "image/jpeg")]
    public async Task GetAsync_WhenPreviewDocumented_ExposesAnonymousReadOnlyContract(
        string suffix,
        string contentType)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var document = await client.GetFromJsonAsync<JsonDocument>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The OpenAPI document is empty.");
        var path = document.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/shared-wishlists/{shareLinkId}/preview" + suffix);

        // Assert
        foreach (var method in new[]
        {
            "get",
            "head"
        })
        {
            var operation = path.GetProperty(method);
            var responses = operation.GetProperty("responses");
            Assert.True(responses.GetProperty("200").GetProperty("content").TryGetProperty(
                contentType,
                out _));
            Assert.True(responses.TryGetProperty(
                "404",
                out _));
            Assert.False(responses.TryGetProperty(
                "401",
                out _));
            Assert.False(operation.TryGetProperty(
                "requestBody",
                out _));
            Assert.DoesNotContain(operation.GetProperty("parameters").EnumerateArray(),
                parameter => parameter.GetProperty("name").GetString() == "X-MonKado-Share-Token");
        }
    }
}
