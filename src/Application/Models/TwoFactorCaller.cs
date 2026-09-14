using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains only identifiers taken from an already authenticated Bearer principal.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorCaller
{
    /// <summary>Gets the authenticated subject.</summary>
    public Guid MemberId
    {
        get; init;
    }

    /// <summary>Gets the registered access-token identifier.</summary>
    public Guid AccessTokenId
    {
        get; init;
    }
}
