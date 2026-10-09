using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class OwnedGiftReservationsOpenApiTests
{
    [Theory]
    [InlineData("get")]
    [InlineData("put")]
    [InlineData("delete")]
    public async Task GetAsync_WhenOwnerReservationIsDocumented_RequiresPrivateAuthenticationWithoutShareToken(string method)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/wishlists/{wishlistId}/wishes/{wishId}/reservations/current")
            .GetProperty(method);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.True(operation.TryGetProperty(
            "security",
            out _));
        var parameters = operation.GetProperty("parameters").EnumerateArray();
        Assert.DoesNotContain(
            parameters,
            parameter => parameter.GetProperty("name").GetString() == "X-MonKado-Share-Token");

        if (method != "get")
            Assert.Contains(
                parameters,
                parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");

        if (method == "put")
        {
            var created = operation.GetProperty("responses").GetProperty("201");
            Assert.True(created.GetProperty("headers").TryGetProperty(
                "Location",
                out _));
            Assert.True(created.GetProperty("headers").TryGetProperty(
                "ETag",
                out _));
        }
    }
}
