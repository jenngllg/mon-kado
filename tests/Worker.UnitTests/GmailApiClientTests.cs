using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JennGllg.Fr.MonKado.Back.Worker.Email;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public sealed class GmailApiClientTests
{
    private static readonly Uri MessagesEndpoint = new("https://gmail.test/messages/send");

    [Fact]
    public async Task SuccessfulRequestPostsRawMessageAndReturnsProviderIdentifier()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using HttpClient httpClient = CreateHttpClient(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(HttpStatusCode.OK, """{"id":"gmail-message-id"}""");
        });
        using GmailApiClient client = new(httpClient, MessagesEndpoint, TimeProvider.System);

        string identifier = await client.SendAsync("raw-message", TestContext.Current.CancellationToken);

        Assert.Equal("gmail-message-id", identifier);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal(MessagesEndpoint, capturedRequest.RequestUri);
        using JsonDocument document = JsonDocument.Parse(capturedBody!);
        Assert.Equal("raw-message", document.RootElement.GetProperty("raw").GetString());
    }

    [Fact]
    public async Task FailedRequestPreservesDeltaRetryAfter()
    {
        using HttpClient httpClient = CreateHttpClient((_, _) =>
        {
            HttpResponseMessage response = JsonResponse(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(7));
            return Task.FromResult(response);
        });
        using GmailApiClient client = new(httpClient, MessagesEndpoint, TimeProvider.System);

        GmailRequestException exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync("raw-message", TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(7), exception.RetryAfter);
    }

    [Fact]
    public async Task FailedRequestCalculatesDateRetryAfterFromTimeProvider()
    {
        DateTimeOffset now = new(2026, 8, 13, 16, 0, 0, TimeSpan.Zero);
        using HttpClient httpClient = CreateHttpClient((_, _) =>
        {
            HttpResponseMessage response = JsonResponse(HttpStatusCode.ServiceUnavailable, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(9));
            return Task.FromResult(response);
        });
        using GmailApiClient client = new(httpClient, MessagesEndpoint, new FixedTimeProvider(now));

        GmailRequestException exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync("raw-message", TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromMinutes(9), exception.RetryAfter);
    }

    [Fact]
    public async Task PastRetryDateIsReportedAsZero()
    {
        DateTimeOffset now = new(2026, 8, 13, 16, 0, 0, TimeSpan.Zero);
        using HttpClient httpClient = CreateHttpClient((_, _) =>
        {
            HttpResponseMessage response = JsonResponse(HttpStatusCode.ServiceUnavailable, "{}");
            response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(-1));
            return Task.FromResult(response);
        });
        using GmailApiClient client = new(httpClient, MessagesEndpoint, new FixedTimeProvider(now));

        GmailRequestException exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync("raw-message", TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.Zero, exception.RetryAfter);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"id":""}""")]
    public async Task SuccessfulResponseRequiresProviderIdentifier(string responseBody)
    {
        using HttpClient httpClient = CreateHttpClient((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, responseBody)));
        using GmailApiClient client = new(httpClient, MessagesEndpoint, TimeProvider.System);

        GmailRequestException exception = await Assert.ThrowsAsync<GmailRequestException>(() =>
            client.SendAsync("raw-message", TestContext.Current.CancellationToken));

        Assert.Null(exception.StatusCode);
        Assert.Null(exception.RetryAfter);
    }

    [Fact]
    public void ProductionConstructorCreatesOfficialGmailClient()
    {
        using GmailApiClient client = new(Options.Create(new GmailOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RefreshToken = "refresh-token"
        }));

        Assert.IsType<IGmailApiClient>(client, exactMatch: false);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new StubHttpMessageHandler(handler));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => currentTime;
    }
}
