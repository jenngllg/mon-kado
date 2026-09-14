using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes an incomplete sign-in without granting an authenticated session.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorChallengeResponse
{
    /// <summary>Gets the short-lived opaque proof retained only in client memory.</summary>
    public string Flow { get; init; } = string.Empty;

    /// <summary>Gets the next permitted action.</summary>
    public TwoFactorRequiredAction RequiredAction
    {
        get; init;
    }

    /// <summary>Gets the absolute expiration in UTC.</summary>
    public DateTime ExpiresAt
    {
        get; init;
    }
}
