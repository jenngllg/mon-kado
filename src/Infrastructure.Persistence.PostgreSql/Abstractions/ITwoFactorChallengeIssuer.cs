using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Stages first-factor-bound challenges inside the caller's account-locked transaction.</summary>
public interface ITwoFactorChallengeIssuer
{
    /// <summary>Stages a challenge when the current role or existing enrollment requires a second factor.</summary>
    /// <param name="member">The first-factor-authenticated, locked account.</param>
    /// <param name="challengeId">The stable operation identifier.</param>
    /// <param name="isPersistent">The initial remembered-session choice.</param>
    /// <param name="previousSessionId">The proven browser session to preserve until completion.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A staged challenge response, or null when the member does not require a second factor.</returns>
    Task<TwoFactorChallengeResponse?> StageAsync(
        MonKadoUser member,
        Guid challengeId,
        bool isPersistent,
        Guid? previousSessionId,
        CancellationToken cancellationToken);

    /// <summary>Checks an exact issued proof in an independent scope after an ambiguous commit.</summary>
    /// <param name="challengeId">The operation identifier.</param>
    /// <param name="memberId">The expected account.</param>
    /// <param name="flow">The exact proof returned by the attempted operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the exact live challenge is durably stored.</returns>
    Task<bool> IsIssuedAsync(
        Guid challengeId,
        Guid memberId,
        string flow,
        CancellationToken cancellationToken);
}
