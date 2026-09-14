using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

/// <summary>Holds account-locked state for one transactional second-factor operation.</summary>
/// <param name="member">The locked account.</param>
/// <param name="factor">The account-wide authenticator state.</param>
/// <param name="challenge">The locked, validated operation grant.</param>
[ExcludeFromCodeCoverage]
public class TwoFactorOperationState(
    MonKadoUser member,
    MemberTwoFactor factor,
    TwoFactorChallenge challenge)
{
    /// <summary>Gets the locked account.</summary>
    public MonKadoUser Member { get; } = member;

    /// <summary>Gets the account-wide authenticator state.</summary>
    public MemberTwoFactor Factor { get; } = factor;

    /// <summary>Gets the locked operation grant.</summary>
    public TwoFactorChallenge Challenge { get; } = challenge;
}
