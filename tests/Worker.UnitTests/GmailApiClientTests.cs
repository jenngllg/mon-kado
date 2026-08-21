using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;

using Microsoft.Extensions.Options;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class GmailApiClientTests
{
    private static readonly Uri _messagesEndpoint = new("https://gmail.test/messages/send");

    [Fact]
    public async Task SendAsync_WhenSuccessfulRequestPostsRawMessageAnd_ReturnsProviderIdentifier()
    {
        // Arrange
        var capturedRequest = default(HttpRequestMessage);
        var capturedBody = default(string);
        using var httpClient = CreateHttpClient(async (
            request,
            cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return JsonResponse(
                HttpStatusCode.OK,
                """{"id":"gmail-message-id"}""");
        });
        using var client = new GmailApiClient(
            httpClient,
            _messagesEndpoint,
            TimeProvider.System);

        // Act
        var identifier = await client.SendAsync(
            "raw-message",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "gmail-message-id",
            identifier);
        Assert.Equal(
            HttpMethod.Post,
            capturedRequest!.Method);
        Assert.Equal(
            _messagesEndpoint,
            capturedRequest.RequestUri);
        using var document = JsonDocument.Parse(capturedBody!);
        Assert.Equal(
            "raw-message",
            document.RootElement.GetProperty("raw").GetString());
    }

    [Fact]
    public async Task SendAsync_WhenFailedRequest_PreservesDeltaRetryAfter()
    {
        // Arrange
        // Act
        using var httpClient = CreateHttpClient((
            _,
            _) =>
        {
            var response = JsonResponse(
                HttpStatusCode.TooManyRequests,
                "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(7));

            return Task.FromResult(response);
        });
        using var client = new GmailApiClient(
            httpClient,
            _messagesEndpoint,
            TimeProvider.System);

        // Assert
        var exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync(
                "raw-message",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            exception.StatusCode);
        Assert.Equal(
            TimeSpan.FromMinutes(7),
            exception.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_WhenFailedRequestHasNoRetryAfter_PreservesMissingRetryAfter()
    {
        // Arrange
        using var httpClient = CreateHttpClient((
            _,
            _) =>
            Task.FromResult(JsonResponse(
                HttpStatusCode.BadRequest,
                "{}")));
        using var client = new GmailApiClient(
            httpClient,
            _messagesEndpoint,
            TimeProvider.System);

        // Act
        var exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync(
                "raw-message",
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(exception.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_WhenFailedRequestCalculatesDateRetryAfterFromTimeProvider_Completes()
    {
        // Arrange
        // Act
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            16,
            0,
            0,
            TimeSpan.Zero);
        using var httpClient = CreateHttpClient((
            _,
            _) =>
        {
            var response = JsonResponse(
                HttpStatusCode.ServiceUnavailable,
                "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(9));

            return Task.FromResult(response);
        });
        using var client = new GmailApiClient(
            httpClient,
            _messagesEndpoint,
            new FixedTimeProvider(now));

        // Assert
        var exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync(
                "raw-message",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            TimeSpan.FromMinutes(9),
            exception.RetryAfter);
    }

    [Fact]
    public async Task SendAsync_WhenPastRetryDate_IsReportedAsZero()
    {
        // Arrange
        // Act
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            16,
            0,
            0,
            TimeSpan.Zero);
        using var httpClient = CreateHttpClient((
            _,
            _) =>
        {
            var response = JsonResponse(
                HttpStatusCode.ServiceUnavailable,
                "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(-1));

            return Task.FromResult(response);
        });
        using var client = new GmailApiClient(
            httpClient,
            _messagesEndpoint,
            new FixedTimeProvider(now));

        // Assert
        var exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync(
                "raw-message",
                TestContext.Current.CancellationToken));

        Assert.Equal(
            TimeSpan.Zero,
            exception.RetryAfter);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"id":""}""")]
    public async Task SendAsync_WhenSuccessfulResponse_RequiresProviderIdentifier(string responseBody)
    {
        // Arrange
        // Act
        using var httpClient = CreateHttpClient((
            _,
            _) =>
            Task.FromResult(JsonResponse(
                HttpStatusCode.OK,
                responseBody)));
        using var client = new GmailApiClient(
            httpClient,
            _messagesEndpoint,
            TimeProvider.System);

        // Assert
        var exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync(
                "raw-message",
                TestContext.Current.CancellationToken));

        Assert.Null(exception.StatusCode);
        Assert.Null(exception.RetryAfter);
    }

    [Fact]
    public void SendAsync_WhenProductionConstructor_CreatesOfficialGmailClient()
    {
        // Arrange
        // Act
        using var client = new GmailApiClient(
            Microsoft.Extensions.Options.Options.Create(new GmailOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RefreshToken = "refresh-token"
            }));

        // Assert
        Assert.IsType<IGmailApiClient>(
            client,
            exactMatch: false);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {

        return new(new StubHttpMessageHandler(handler));
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {

        return new(statusCode)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

}
