using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes second-factor enrollment without exposing security material.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorStatusResponse
{
    /// <summary>Gets whether the member has a confirmed authenticator.</summary>
    public bool IsEnabled
    {
        get; init;
    }

    /// <summary>Gets the number of unconsumed recovery codes for the current authenticator.</summary>
    public int RemainingRecoveryCodes
    {
        get; init;
    }
}
