namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Manages the persistent browser guest-session cookie.
/// </summary>
public interface IGuestSessionCookieService
{
    /// <summary>Reads the guest token from a request.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The token when present.</returns>
    string? GetValue(HttpRequest request);

    /// <summary>Appends a persistent guest token cookie.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="token">The opaque guest token.</param>
    /// <param name="expiresAt">The absolute UTC expiration.</param>
    void Append(
        HttpContext context,
        string token,
        DateTime expiresAt);

    /// <summary>Deletes the guest token cookie.</summary>
    /// <param name="context">The HTTP context.</param>
    void Delete(HttpContext context);
}
