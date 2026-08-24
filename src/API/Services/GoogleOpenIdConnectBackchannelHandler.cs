using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Classifies transient Google backchannel HTTP responses without retrying non-idempotent token exchanges.
/// </summary>
/// <remarks>
/// Initializes a handler around the supplied transport.
/// </remarks>
/// <param name="innerHandler">The underlying Google HTTP transport.</param>
public class GoogleOpenIdConnectBackchannelHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    /// <summary>
    /// Initializes a handler backed by the platform HTTP transport.
    /// </summary>
    public GoogleOpenIdConnectBackchannelHandler()
        : this(new HttpClientHandler())
    {
    }

    /// <summary>
    /// Sends one Google backchannel request and classifies transient HTTP statuses.
    /// </summary>
    /// <param name="request">The outbound request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The non-transient provider response.</returns>
    /// <exception cref="HttpRequestException">Google returned a transient HTTP status.</exception>
    /// <exception cref="OperationCanceledException">The request was canceled.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(
            request,
            cancellationToken);

        if (!IsTransient(response.StatusCode))
            return response;

        var statusCode = response.StatusCode;
        response.Dispose();

        throw new HttpRequestException(
            "Google authentication backchannel returned a transient HTTP status.",
            null,
            statusCode);
    }

    /// <summary>
    /// Identifies HTTP statuses that represent a retryable provider outage.
    /// </summary>
    /// <param name="statusCode">The provider status.</param>
    /// <returns><see langword="true" /> for 408, 429 and 5xx responses.</returns>
    private static bool IsTransient(HttpStatusCode statusCode)
    {

        return statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            (int)statusCode >= StatusCodes.Status500InternalServerError;
    }
}
