using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class UserSearchTests
{
    [Fact]
    public async Task SearchAsync_WhenSuccessful_DoesNotLogSearchTermOrResult()
    {
        // Arrange
        const string Term = "SensitiveSearchName";
        var service = new RecordingUserSearchService();
        await using var baseline = new RegistrationApiFactory();
        await using var factory = baseline.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IUserSearchService>();
                    services.AddSingleton<IUserSearchService>(service);
                }));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members?displayName=" + Term,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            Term,
            service.LastTerm);
        Assert.Contains(
            baseline.LogMessages,
            message => message.Contains(
                "Public member search completed.",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            baseline.LogMessages,
            message => message.Contains(
                Term,
                StringComparison.Ordinal) || message.Contains(
                "ConfidentialResultName",
                StringComparison.Ordinal));
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    private static readonly string[] _publicProperties = [
        "id",
        "displayName"
    ];
    [Theory]
    [InlineData("")]
    [InlineData("?displayName=")]
    [InlineData("?displayName=%20%20")]
    [InlineData("?displayName=j")]
    [InlineData("?displayName=je%0An")]
    [InlineData("?displayName=jen&page=0")]
    [InlineData("?displayName=jen&pageSize=0")]
    [InlineData("?displayName=jen&pageSize=101")]
    [InlineData("?displayName=jen&page=abc")]
    public async Task SearchAsync_WhenQueryIsInvalid_ReturnsStructuredBadRequest(string query)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members" + query,
            TestContext.Current.CancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            400,
            json
                .GetProperty("statusCode")
                .GetInt32());
    }

    [Fact]
    public async Task SearchAsync_WhenDatabaseIsUnavailable_ReturnsStructuredServiceUnavailable()
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members?displayName=jen",
            TestContext.Current.CancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            503,
            json
                .GetProperty("statusCode")
                .GetInt32());
    }

    [Fact]
    public async Task SearchAsync_WhenAddressExceedsQuota_ReturnsTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            using var invalid = await client.GetAsync(
                "/api/v1/members?displayName=j",
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.BadRequest,
                invalid.StatusCode);
        }

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members?displayName=jen",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task OpenApiAsync_WhenSearchIsDocumented_ExposesPublicMinimalContract()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        var document = await client.GetFromJsonAsync<JsonElement>(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);

        // Assert
        var operation = document
            .GetProperty("paths")
            .GetProperty("/api/v1/members")
            .GetProperty("get");
        Assert.False(operation
                .GetProperty("responses")
                .TryGetProperty(
                "401",
                out _));
        Assert.True(operation
                .GetProperty("responses")
                .TryGetProperty(
                "503",
                out _));
        var properties = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("UserSearchResponse")
            .GetProperty("properties");
        Assert.Equal(
            _publicProperties,
            properties
                .EnumerateObject()
                .Select(property => property.Name));
    }
}
