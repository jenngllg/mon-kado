using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Manages the browser refresh token cookie.
/// </summary>
public interface IRefreshTokenCookieService
{
    /// <summary>
    /// Reads the refresh token from a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The refresh token when present.</returns>
    string? GetValue(HttpRequest request);

    /// <summary>
    /// Appends a refresh token cookie to a response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="tokens">The session tokens.</param>
    void Append(
        HttpContext context,
        AccountSessionTokens tokens);

    /// <summary>
    /// Appends a refresh-only session cookie after a browser callback.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="session">The refresh-only session.</param>
    void Append(
        HttpContext context,
        AccountRefreshSession session);

    /// <summary>
    /// Deletes the refresh token cookie.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    void Delete(HttpContext context);
}
