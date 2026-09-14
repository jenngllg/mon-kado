using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents an account login result and its optional tokens.
/// </summary>
/// <param name="result">The login outcome.</param>
/// <param name="tokens">The created tokens.</param>
[ExcludeFromCodeCoverage]
public class AccountSessionLoginResult(
    AccountLoginResult result,
    AccountSessionTokens? tokens)
{
    /// <summary>Gets the incomplete sign-in proof when no complete session may yet be created.</summary>
    public TwoFactorChallengeResponse? Challenge
    {
        get; init;
    }

    /// <summary>
    /// Gets the login outcome.
    /// </summary>
    public AccountLoginResult Result { get; } = result;

    /// <summary>
    /// Gets the created tokens.
    /// </summary>
    public AccountSessionTokens? Tokens { get; } = tokens;
}
