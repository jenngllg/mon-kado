using JennGllg.Fr.MonKado.Back.Api.Abstractions;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Manages the hardened persistent browser guest-session cookie.
/// </summary>
/// <param name="environment">The web host environment.</param>
public class GuestSessionCookieService(IWebHostEnvironment environment)
    : IGuestSessionCookieService
{
    /// <summary>Gets the cookie name used outside production.</summary>
    public const string LocalCookieName = "MonKado.Guest";

    /// <summary>Gets the cookie name used in production.</summary>
    public const string ProductionCookieName = "__Host-MonKado.Guest";

    /// <inheritdoc />
    public string? GetValue(HttpRequest request)
    {
        return request.Cookies[GetCookieName()];
    }

    /// <inheritdoc />
    public void Append(
        HttpContext context,
        string token,
        DateTime expiresAt)
    {
        context.Response.Cookies.Append(
            GetCookieName(),
            token,
            CreateCookieOptions(
                context.Request.IsHttps,
                new DateTimeOffset(
                    expiresAt,
                    TimeSpan.Zero)));
    }

    /// <inheritdoc />
    public void Delete(HttpContext context)
    {
        context.Response.Cookies.Delete(
            GetCookieName(),
            CreateCookieOptions(
                context.Request.IsHttps,
                null));
    }

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

    private string GetCookieName()
    {
        return environment.IsProduction()
            ? ProductionCookieName
            : LocalCookieName;
    }
}
