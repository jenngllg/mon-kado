using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class GeneralRateLimitTests
{
    [Theory]
    [InlineData("/api/v1/wishlists")]
    [InlineData("/security/csrf-token")]
    public async Task OpenApi_WhenPerimeterApplies_DocumentsStructuredRejection(string path)
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        var rejection = document.RootElement.GetProperty("paths")
            .GetProperty(path)
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("429");
        Assert.Contains(
            "Retry-After",
            rejection.GetProperty("description").GetString());
        Assert.True(rejection.GetProperty("content").TryGetProperty(
            "application/json",
            out _));
    }

    [Fact]
    public async Task ExecuteAsync_WhenMembersShareAddress_KeepsBusinessQuotasIndependent()
    {
        // Arrange
        using var factory = new SecurityApiFactory();
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<IAccessTokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            tokens.Create(Guid.CreateVersion7()).Value);

        // Act
        for (var index = 0; index < 10; index++)
        {
            using var response = await client.GetAsync(
                "/api/v1/_tests/security/member-quota",
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode);
        }

        using var rejected = await client.GetAsync(
            "/api/v1/_tests/security/member-quota",
            TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            tokens.Create(Guid.CreateVersion7()).Value);
        using var otherMember = await client.GetAsync(
            "/api/v1/_tests/security/member-quota",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            otherMember.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUntrustedCallerChangesForwardedAddress_DoesNotResetQuota()
    {
        // Arrange
        using var factory = new SecurityApiFactory(
            remoteIpAddress: IPAddress.Parse("192.0.2.1"),
            generalPermitLimit: 1);
        using var client = factory.CreateClient();
        using var first = new HttpRequestMessage(
            HttpMethod.Get,
            "/security/csrf-token");
        first.Headers.Add(
            "X-Forwarded-For",
            "198.51.100.1");
        using var second = new HttpRequestMessage(
            HttpMethod.Get,
            "/security/csrf-token");
        second.Headers.Add(
            "X-Forwarded-For",
            "198.51.100.2");

        // Act
        using var accepted = await client.SendAsync(
            first,
            TestContext.Current.CancellationToken);
        using var rejected = await client.SendAsync(
            second,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            accepted.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
    }

    [Fact]
    public async Task ExecuteAsync_WhenQuotaExhausted_DoesNotValidateAnotherBearer()
    {
        // Arrange
        using var factory = new SecurityApiFactory(generalPermitLimit: 1);
        using var client = factory.CreateClient();
        var tokens = factory.Services.GetRequiredService<IAccessTokenService>();
        var validation = Assert.IsType<RecordingAuthenticatedMemberValidationService>(
            factory.Services.GetRequiredService<IAuthenticatedMemberValidationService>());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            tokens.Create(Guid.CreateVersion7()).Value);
        using var accepted = await client.GetAsync(
            "/api/v1/_tests/security/bearer",
            TestContext.Current.CancellationToken);

        // Act
        using var rejected = await client.GetAsync(
            "/api/v1/_tests/security/bearer",
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            accepted.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.Equal(
            1,
            validation.Calls);
        Assert.True(rejected.Headers.CacheControl?.NoStore);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(
            429,
            document.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(
            "REQUEST_RATE_LIMIT_EXCEEDED",
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.True(rejected.Headers.Contains("X-Correlation-ID"));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(300, 0)]
    [InlineData(300, 3601)]
    public void CreateClient_WhenQuotaConfigurationIsInvalid_RefusesStartup(
        int permits,
        int seconds)
    {
        // Arrange
        using var factory = new SecurityApiFactory(
            generalPermitLimit: permits,
            generalWindowSeconds: seconds);

        // Act / Assert
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCsrfQuotaExhausted_LeavesHealthAndPreflightAvailable()
    {
        // Arrange
        using var factory = new SecurityApiFactory(generalPermitLimit: 1);
        using var client = factory.CreateClient();
        using var first = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        using var options = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/wishlists");
        options.Headers.Add(
            "Origin",
            "http://localhost:5173");
        options.Headers.Add(
            "Access-Control-Request-Method",
            "GET");

        // Act
        using var rejected = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        using var health = await client.GetAsync(
            "/liveness",
            TestContext.Current.CancellationToken);
        using var preflight = await client.SendAsync(
            options,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            first.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            health.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            preflight.StatusCode);
    }
}
