using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Email;

internal sealed class GmailApiClient : IGmailApiClient, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient httpClient;
    private readonly Uri messagesEndpoint;
    private readonly GmailService? ownedService;
    private readonly TimeProvider timeProvider;

    public GmailApiClient(IOptions<GmailOptions> options)
    {
        GmailOptions gmail = options.Value;
        GoogleAuthorizationCodeFlow flow = new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = gmail.ClientId,
                ClientSecret = gmail.ClientSecret
            },
            Scopes = [GmailService.Scope.GmailSend]
        });
        UserCredential credential = new(
            flow,
            "mon-kado-authentication-email-sender",
            new TokenResponse { RefreshToken = gmail.RefreshToken });
        GmailService service = new(new BaseClientService.Initializer
        {
            ApplicationName = "MonKado",
            HttpClientInitializer = credential
        });
        service.HttpClient.Timeout = RequestTimeout;
        httpClient = service.HttpClient;
        messagesEndpoint = new Uri(
            new Uri(service.BaseUri, UriKind.Absolute),
            $"{service.BasePath}users/me/messages/send");
        ownedService = service;
        timeProvider = TimeProvider.System;
    }

    internal GmailApiClient(
        HttpClient httpClient,
        Uri messagesEndpoint,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(messagesEndpoint);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.httpClient = httpClient;
        this.messagesEndpoint = messagesEndpoint;
        this.timeProvider = timeProvider;
    }

    public async Task<string> SendAsync(string rawMessage, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            messagesEndpoint,
            new { raw = rawMessage },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            if (response.Headers.RetryAfter?.Date is { } retryAt)
            {
                TimeSpan dateDelay = retryAt - timeProvider.GetUtcNow();
                retryAfter = dateDelay > TimeSpan.Zero ? dateDelay : TimeSpan.Zero;
            }

            throw new GmailRequestException(response.StatusCode, retryAfter);
        }

        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("id", out JsonElement idElement) ||
            string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            throw new GmailRequestException(statusCode: null, retryAfter: null);
        }

        return idElement.GetString()!;
    }

    public void Dispose()
    {
        ownedService?.Dispose();
    }
}
