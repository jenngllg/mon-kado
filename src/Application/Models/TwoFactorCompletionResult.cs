using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains either a confirmed session or a required authenticator-replacement challenge.</summary>
[ExcludeFromCodeCoverage]
public class TwoFactorCompletionResult
{
    /// <summary>Gets tokens only after second-factor completion has been committed.</summary>
    public AccountSessionTokens? Tokens
    {
        get; init;
    }

    /// <summary>Gets the next required step when recovery does not yet permit a full session.</summary>
    public TwoFactorChallengeResponse? Challenge
    {
        get; init;
    }
}
