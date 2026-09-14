namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Stages security-event revocations while preserving the one grant performing the operation.</summary>
public interface ITwoFactorRevocationService
{
    /// <summary>Revokes sessions and other pending grants inside the caller's account-locked transaction.</summary>
    /// <param name="memberId">The account whose credentials changed.</param>
    /// <param name="preservedChallengeId">The sole challenge allowed to finish.</param>
    /// <param name="now">The UTC revocation time.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A task completed when revocations are staged, without saving or committing.</returns>
    Task RevokeOtherAsync(
        Guid memberId,
        Guid preservedChallengeId,
        DateTime now,
        CancellationToken cancellationToken);
}
