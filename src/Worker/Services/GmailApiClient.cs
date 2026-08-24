using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Options;

using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Worker.Services;

/// <summary>
/// Sends raw messages through the Gmail API.
/// </summary>
public sealed class GmailApiClient : IGmailApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _messagesEndpoint;
    private readonly GmailService? _ownedService;
    private readonly TimeProvider _timeProvider;
    /// <summary>
    /// Initializes a new instance of the class.
    /// </summary>
    /// <param name="options">The options.</param>

    public GmailApiClient(IOptions<GmailOptions> options)
    {
        var gmail = options.Value;
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = gmail.ClientId,
                ClientSecret = gmail.ClientSecret
            },
            Scopes = [GmailService.Scope.GmailSend]
        });
        var credential = new UserCredential(
            flow,
            "mon-kado-authentication-email-sender",
            new TokenResponse { RefreshToken = gmail.RefreshToken });
        var service = new GmailService(new BaseClientService.Initializer
        {
            ApplicationName = "MonKado",
            HttpClientInitializer = credential
        });
        service.HttpClient.Timeout = gmail.RequestTimeout;
        _httpClient = service.HttpClient;
        _messagesEndpoint = new Uri(
            new Uri(
                service.BaseUri,
                UriKind.Absolute),
            $"{service.BasePath}users/me/messages/send");
        _ownedService = service;
        _timeProvider = TimeProvider.System;
    }

    /// <summary>
    /// Initializes a Gmail client with explicit HTTP dependencies.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to call Gmail.</param>
    /// <param name="messagesEndpoint">The Gmail messages endpoint.</param>
    /// <param name="timeProvider">The time provider used to evaluate retry delays.</param>
    public GmailApiClient(
        HttpClient httpClient,
        Uri messagesEndpoint,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(messagesEndpoint);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _httpClient = httpClient;
        _messagesEndpoint = messagesEndpoint;
        _timeProvider = timeProvider;
    }
    /// <summary>
    /// Executes the send async operation.
    /// </summary>
    /// <param name="rawMessage">The raw message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task<string> SendAsync(
        string rawMessage,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            _messagesEndpoint,
            new
            {
                raw = rawMessage
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;

            if (response.Headers.RetryAfter?.Date is { } retryAt)
            {
                var dateDelay = retryAt - _timeProvider.GetUtcNow();
                retryAfter = dateDelay > TimeSpan.Zero ? dateDelay : TimeSpan.Zero;
            }

            throw new GmailRequestException(
                response.StatusCode,
                retryAfter);
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            content,
            cancellationToken: cancellationToken);

        return !document.RootElement.TryGetProperty(
            "id",
            out var idElement) ||
            string.IsNullOrWhiteSpace(idElement.GetString())
            ? throw new GmailRequestException(
                statusCode: null,
                retryAfter: null)
            : idElement.GetString()!;
    }
    /// <summary>
    /// Executes the dispose operation.
    /// </summary>

    public void Dispose()
    {
        _ownedService?.Dispose();
    }
}
