using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Stages security-event revocations without invalidating the currently completing grant.</summary>
/// <param name="context">The shared account-locked transaction context.</param>
public class TwoFactorRevocationService(MonKadoDbContext context) : ITwoFactorRevocationService
{
    /// <inheritdoc/>
    public async Task RevokeOtherAsync(
        Guid memberId,
        Guid preservedChallengeId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await context.AuthenticationSessions
            .Where(session => session.UserId == memberId && session.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.RevokedAt,
                    now),
                cancellationToken);
        await context.TwoFactorChallenges
            .Where(challenge => challenge.MemberId == memberId && challenge.Id != preservedChallengeId &&
                challenge.ConsumedAt == null && challenge.InvalidatedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        challenge => challenge.InvalidatedAt,
                        now)
                    .SetProperty(
                        challenge => challenge.PendingCredentialId,
                        (Guid?)null)
                    .SetProperty(
                        challenge => challenge.PendingProtectedSecret,
                        (string?)null)
                    .SetProperty(
                        challenge => challenge.ProtectedGoogleContext,
                        (string?)null),
                cancellationToken);
    }
}
