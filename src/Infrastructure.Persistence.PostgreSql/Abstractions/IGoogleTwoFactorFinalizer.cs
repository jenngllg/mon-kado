using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Rechecks and stages deferred Google associations only after successful MonKado MFA.</summary>
public interface IGoogleTwoFactorFinalizer
{
    /// <summary>Applies validated Google authority without saving, committing or issuing a session.</summary>
    /// <param name="member">The account locked by the completing second-factor transaction.</param>
    /// <param name="challenge">The proved challenge carrying encrypted Google authority.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A task completed when the association and security changes are staged.</returns>
    Task FinalizeAsync(
        MonKadoUser member,
        TwoFactorChallenge challenge,
        CancellationToken cancellationToken);
}
