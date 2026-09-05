using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishlistReportTests
{
    private const string ShareSecret = "public-secret";

    [Theory]
    [InlineData("spamOrScam", WishlistReportReason.SpamOrScam)]
    [InlineData("inappropriateContent", WishlistReportReason.InappropriateContent)]
    [InlineData("privacyViolation", WishlistReportReason.PrivacyViolation)]
    [InlineData("other", WishlistReportReason.Other)]
    public async Task ReportAsync_WhenRequestIsValid_ReturnsNoContentAndCreatesAnonymousReport(
        string reason,
        WishlistReportReason expectedReason)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();
        var details = expectedReason is WishlistReportReason.Other
            ? "  Additional de\u0301tails  "
            : null;

        // Act
        using var response = await SendReportAsync(
            client,
            shareLinkId,
            reason,
            details);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "noindex, nofollow, noarchive",
            Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal(
            0,
            response.Content.Headers.ContentLength);
        var creation = Assert.Single(factory.WishlistReportService.Creations);
        Assert.Equal(
            7,
            creation.ReportId.Version);
        Assert.Equal(
            shareLinkId,
            creation.ShareLinkId);
        Assert.Equal(
            ShareSecret,
            creation.ShareSecret);
        Assert.Equal(
            expectedReason,
            creation.Reason);
        Assert.Equal(
            expectedReason is WishlistReportReason.Other
                ? "Additional détails"
                : null,
            creation.Details);
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                ShareSecret,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "Additional",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}", "reason")]
    [InlineData("{\"reason\":\"other\",\"details\":null}", "details")]
    [InlineData("{\"reason\":\"unknown\",\"details\":null}", "$.reason")]
    [InlineData("{\"reason\":0,\"details\":null}", "$.reason")]
    public async Task ReportAsync_WhenBodyIsInvalid_ReturnsStructuredBadRequest(
        string json,
        string expectedPropertyName)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateRequest(
            Guid.CreateVersion7(),
            csrfToken);
        request.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "REQUEST_VALIDATION_ERROR",
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Contains(
            document.RootElement.GetProperty("validationErrors").EnumerateArray(),
            error => error.GetProperty("propertyName").GetString() == expectedPropertyName);
        Assert.Empty(factory.WishlistReportService.Creations);
    }

    [Fact]
    public async Task ReportAsync_WhenAntiforgeryTokenIsMissing_ReturnsBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            Guid.CreateVersion7(),
            null);
        request.Content = JsonContent.Create(new
        {
            reason = "spamOrScam",
            details = (string?)null
        });

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Empty(factory.WishlistReportService.Creations);
    }

    [Fact]
    public async Task ReportAsync_WhenShareSecretIsMissing_ReturnsNonDisclosingNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateRequest(
            Guid.CreateVersion7(),
            csrfToken,
            includeShareToken: false);
        request.Content = JsonContent.Create(new
        {
            reason = "spamOrScam",
            details = (string?)null
        });

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            "SHARED_WISHLIST_NOT_FOUND",
            document.RootElement.GetProperty("errorCode").GetString());
        Assert.Empty(factory.WishlistReportService.Creations);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.NotFound, "SHARED_WISHLIST_NOT_FOUND")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "TECHNICAL_DEPENDENCY_UNAVAILABLE")]
    public async Task ReportAsync_WhenServiceFails_ReturnsStructuredError(
        bool shareLinkIsInvalid,
        HttpStatusCode expectedStatusCode,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistReportService.Exception = shareLinkIsInvalid
            ? new SharedWishlistNotFoundException()
            : new DependencyUnavailableException(
                "PostgreSQL",
                null);
        using var client = factory.CreateClient();

        // Act
        using var response = await SendReportAsync(
            client,
            Guid.CreateVersion7(),
            "spamOrScam",
            null);

        // Assert
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The error response is empty.");
        Assert.Equal(
            expectedErrorCode,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ReportAsync_WhenRateLimitIsExceeded_ReturnsTooManyRequests()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory(
            remoteIpAddress: IPAddress.Parse("192.0.2.89"));
        using var client = factory.CreateClient();
        var shareLinkId = Guid.CreateVersion7();
        var csrfToken = await GetCsrfTokenAsync(client);
        var permitLimit = AuthenticationRateLimitingExtensions.SharedWishlistReportPermitLimit;
        HttpResponseMessage? response = null;

        try
        {
            // Act
            for (var requestNumber = 0;
                requestNumber <= permitLimit;
                requestNumber++)
            {
                var useUppercaseShareLinkId = requestNumber == permitLimit;
                response?.Dispose();
                response = await SendReportAsync(
                    client,
                    shareLinkId,
                    "spamOrScam",
                    null,
                    csrfToken,
                    useUppercaseShareLinkId);
            }

            // Assert
            Assert.NotNull(response);
            Assert.Equal(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
                TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The error response is empty.");
            Assert.Equal(
                "REQUEST_RATE_LIMIT_EXCEEDED",
                document.RootElement.GetProperty("errorCode").GetString());
            Assert.Equal(
                permitLimit,
                factory.WishlistReportService.Creations.Count);
        }
        finally
        {
            response?.Dispose();
        }
    }

    [Fact]
    public async Task ReportAsync_WhenMediaTypeIsUnsupported_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateRequest(
            Guid.CreateVersion7(),
            csrfToken);
        request.Content = new StringContent(
            "reason=spamOrScam",
            Encoding.UTF8,
            "text/plain");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Empty(factory.WishlistReportService.Creations);
    }

    [Fact]
    public async Task ReportAsync_WhenPayloadIsTooLarge_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateRequest(
            Guid.CreateVersion7(),
            csrfToken);
        request.Content = JsonContent.Create(new
        {
            reason = "other",
            details = new string(
                'a',
                5 * 1024)
        });

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Empty(factory.WishlistReportService.Creations);
    }

    private static async Task<HttpResponseMessage> SendReportAsync(
        HttpClient client,
        Guid shareLinkId,
        string reason,
        string? details,
        string? csrfToken = null,
        bool useUppercaseShareLinkId = false)
    {
        csrfToken ??= await GetCsrfTokenAsync(client);
        using var request = CreateRequest(
            shareLinkId,
            csrfToken,
            useUppercaseShareLinkId: useUppercaseShareLinkId);
        request.Content = JsonContent.Create(new
        {
            reason,
            details
        });

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage CreateRequest(
        Guid shareLinkId,
        string? csrfToken,
        bool includeShareToken = true,
        bool useUppercaseShareLinkId = false)
    {
        var shareLinkIdValue = useUppercaseShareLinkId
            ? shareLinkId.ToString().ToUpperInvariant()
            : shareLinkId.ToString();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/shared-wishlists/{shareLinkIdValue}/reports");

        if (includeShareToken)
        {
            request.Headers.TryAddWithoutValidation(
                "X-MonKado-Share-Token",
                ShareSecret);
        }

        if (csrfToken is not null)
        {
            request.Headers.Add(
                WebSecurityOptions.AntiforgeryHeaderName,
                csrfToken);
        }

        return request;
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);

        return response?.Token
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
    }
}
