namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Stores only a recovery-code hash and its exclusive recovery-flow reservation.</summary>
public class TwoFactorRecoveryCode
{
    /// <summary>Gets the application-generated identifier.</summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>Gets the credential owner.</summary>
    public Guid MemberId
    {
        get; private set;
    }

    /// <summary>Gets the credential version for which this code was issued.</summary>
    public Guid CredentialId
    {
        get; private set;
    }

    /// <summary>Gets the SHA-256 hash of the canonical recovery code.</summary>
    public byte[] CodeHash { get; private set; } = [];

    /// <summary>Gets the challenge reserving this code until replacement confirmation.</summary>
    public Guid? ReservedChallengeId
    {
        get; private set;
    }

    /// <summary>Gets the absolute reservation expiration in UTC.</summary>
    public DateTime? ReservedUntil
    {
        get; private set;
    }

    /// <summary>Gets the date the reserved code was consumed by a confirmed replacement.</summary>
    public DateTime? ConsumedAt
    {
        get; private set;
    }

    /// <summary>Creates a hashed recovery credential.</summary>
    /// <param name="id">The new code identifier.</param>
    /// <param name="memberId">The owner.</param>
    /// <param name="credentialId">The authenticator version.</param>
    /// <param name="codeHash">The hash, never the original code.</param>
    /// <returns>The persistable recovery credential.</returns>
    public static TwoFactorRecoveryCode Create(
        Guid id,
        Guid memberId,
        Guid credentialId,
        byte[] codeHash)
    {

        return new TwoFactorRecoveryCode
        {
            Id = id,
            MemberId = memberId,
            CredentialId = credentialId,
            CodeHash = codeHash
        };
    }

    /// <summary>Exclusively reserves a code without consuming it before replacement is confirmed.</summary>
    /// <param name="challengeId">The validated recovery challenge.</param>
    /// <param name="expiresAt">The challenge's original expiration.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>Whether the code was available for this challenge.</returns>
    public bool TryReserve(
        Guid challengeId,
        DateTime expiresAt,
        DateTime now)
    {

        if (ConsumedAt is not null || expiresAt <= now)
            return false;

        if (ReservedUntil is { } reservedUntil && reservedUntil > now)
            return Nullable.Equals(
                ReservedChallengeId,
                challengeId);

        ReservedChallengeId = challengeId;
        ReservedUntil = expiresAt;

        return true;
    }

    /// <summary>Consumes a still-live exclusive reservation when replacement is confirmed.</summary>
    /// <param name="challengeId">The challenge confirming replacement.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>Whether the reserved code was consumed.</returns>
    public bool TryConsume(
        Guid challengeId,
        DateTime now)
    {

        if (ConsumedAt is not null || ReservedUntil is not { } reservedUntil || !Nullable.Equals(
                ReservedChallengeId,
                challengeId) || reservedUntil <= now)
            return false;

        ConsumedAt = now;

        return true;
    }
}
