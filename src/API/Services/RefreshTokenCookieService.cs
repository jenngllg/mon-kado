using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Manages the hardened browser refresh token cookie.
/// </summary>
/// <param name="environment">The web host environment.</param>
public class RefreshTokenCookieService(IWebHostEnvironment environment)
    : IRefreshTokenCookieService
{
    /// <summary>
    /// Gets the refresh token cookie name used outside production.
    /// </summary>
    internal const string LocalCookieName = "MonKado.Refresh";

    /// <summary>
    /// Gets the refresh token cookie name used in production.
    /// </summary>
    internal const string ProductionCookieName = "__Host-MonKado.Refresh";

    /// <summary>
    /// Reads the refresh token from a request cookie.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The refresh token when present.</returns>
    public string? GetValue(HttpRequest request)
    {

        return request.Cookies[GetCookieName()];
    }

    /// <summary>
    /// Appends the refresh token cookie to a response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="tokens">The session tokens.</param>
    public void Append(
        HttpContext context,
        AccountSessionTokens tokens)
    {
        Append(
            context,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt,
            tokens.IsPersistent);
    }

    /// <inheritdoc />
    public void Append(
        HttpContext context,
        AccountRefreshSession session)
    {
        Append(
            context,
            session.RefreshToken,
            session.RefreshTokenExpiresAt,
            session.IsPersistent);
    }

    /// <summary>
    /// Deletes the refresh token cookie.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public void Delete(HttpContext context)
    {
        context.Response.Cookies.Delete(
            GetCookieName(),
            CreateCookieOptions(
                context.Request.IsHttps,
                null));
    }

    /// <summary>
    /// Creates the shared hardened refresh cookie options.
    /// </summary>
    /// <param name="requestIsHttps">Whether the current request uses HTTPS.</param>
    /// <param name="expires">The optional persistent expiration.</param>
    /// <returns>The configured cookie options.</returns>
    [SuppressMessage(
        "Security",
        "S2092:Cookies should be sent over SSL/TLS",
        Justification = "Production cookies are always secure; loopback HTTP remains supported only for local development.")]
    private CookieOptions CreateCookieOptions(
        bool requestIsHttps,
        DateTimeOffset? expires)
    {

        return new CookieOptions
        {
            Expires = expires,
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Strict,
            Secure = environment.IsProduction() || requestIsHttps
        };
    }

    /// <summary>
    /// Gets the environment-specific refresh cookie name.
    /// </summary>
    /// <returns>The refresh cookie name.</returns>
    private string GetCookieName()
    {

        return environment.IsProduction()
            ? ProductionCookieName
            : LocalCookieName;
    }

    /// <summary>
    /// Appends refresh material with the shared hardened cookie policy.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="refreshTokenExpiresAt">The refresh token expiration.</param>
    /// <param name="isPersistent">Whether the browser cookie is persistent.</param>
    private void Append(
        HttpContext context,
        string refreshToken,
        DateTime refreshTokenExpiresAt,
        bool isPersistent)
    {
        var options = CreateCookieOptions(
            context.Request.IsHttps,
            isPersistent
                ? new DateTimeOffset(
                    refreshTokenExpiresAt,
                    TimeSpan.Zero)
                : null);
        context.Response.Cookies.Append(
            GetCookieName(),
            refreshToken,
            options);
    }
}
