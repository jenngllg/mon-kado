using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistSubscriptionOpenApiTests(UnavailablePostgreSqlApiFactory factory) : IClassFixture<UnavailablePostgreSqlApiFactory>
{
    [Theory]
    [InlineData("/api/v1/shared-wishlists/{shareLinkId}/subscriptions", "post", "201")]
    [InlineData("/api/v1/shared-wishlists/{shareLinkId}/subscriptions/current", "get", "200")]
    [InlineData("/api/v1/wishlist-subscriptions", "get", "200")]
    [InlineData("/api/v1/wishlist-subscriptions/{id}", "get", "200")]
    [InlineData("/api/v1/wishlist-subscriptions/{id}", "delete", "204")]
    public async Task GetAsync_WhenSubscriptionContractIsRequested_DescribesSecuredOperations(
        string path,
        string method,
        string success)
    {
        // Arrange
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
            ?? throw new InvalidOperationException("Missing contract.");

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
        var statuses = operation.GetProperty("responses");
        Assert.True(statuses.TryGetProperty(
            success,
            out _));
        Assert.True(statuses.TryGetProperty(
            "401",
            out _));
        Assert.True(statuses.TryGetProperty(
            "500",
            out _));
        Assert.True(operation.GetProperty("security").GetArrayLength() > 0);
        var properties = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("WishlistSubscriptionResponse").GetProperty("properties");
        Assert.True(properties.TryGetProperty(
            "ownerDisplayName",
            out _));
        Assert.True(properties.TryGetProperty(
            "shareUrl",
            out _));
        Assert.False(properties.TryGetProperty(
            "protectedSecret",
            out _));
        Assert.False(properties.TryGetProperty(
            "shareSecretHash",
            out _));
        Assert.False(properties.TryGetProperty(
            "wishes",
            out _));
    }
}
