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
    private readonly GmailService service;

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
        service = new GmailService(new BaseClientService.Initializer
        {
            ApplicationName = "MonKado",
            HttpClientInitializer = credential
        });
        service.HttpClient.Timeout = RequestTimeout;
    }

    public async Task<string> SendAsync(string rawMessage, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await service.HttpClient.PostAsJsonAsync(
            "https://gmail.googleapis.com/gmail/v1/users/me/messages/send",
            new { raw = rawMessage },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            if (response.Headers.RetryAfter?.Date is { } retryAt)
            {
                TimeSpan dateDelay = retryAt - DateTimeOffset.UtcNow;
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
        service.Dispose();
    }
}
