namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Defines the shared MonKado refresh session lifetime policy.
/// </summary>
public static class RefreshSessionPolicy
{
    private static readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan _persistentSessionLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// Gets the initial expiration for a newly created refresh session.
    /// </summary>
    /// <param name="createdAt">The session creation date and time.</param>
    /// <param name="isPersistent">Whether the session is persistent.</param>
    /// <returns>The refresh session expiration.</returns>
    public static DateTime GetInitialExpiration(
        DateTime createdAt,
        bool isPersistent)
    {

        return createdAt.Add(
            isPersistent
                ? _persistentSessionLifetime
                : _sessionLifetime);
    }

    /// <summary>
    /// Gets the expiration after rotating a refresh session.
    /// </summary>
    /// <param name="renewedAt">The renewal date and time.</param>
    /// <param name="currentExpiration">The current fixed expiration.</param>
    /// <param name="isPersistent">Whether the session is persistent.</param>
    /// <returns>The rotated refresh session expiration.</returns>
    public static DateTime GetRotatedExpiration(
        DateTime renewedAt,
        DateTime currentExpiration,
        bool isPersistent)
    {

        return isPersistent
            ? currentExpiration
            : renewedAt.Add(_sessionLifetime);
    }
}
