using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Tracks a short-lived, purpose-bound proof without persisting its bearer value.</summary>
public class TwoFactorChallenge
{
    /// <summary>Gets the application-generated operation identifier.</summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>Gets the expected member, never selected by the continuation client.</summary>
    public Guid MemberId
    {
        get; private set;
    }

    /// <summary>Gets the SHA-256 hash of the opaque flow.</summary>
    public byte[] FlowHash { get; private set; } = [];

    /// <summary>Gets the operation for which the proof is valid.</summary>
    public TwoFactorFlowPurpose Purpose
    {
        get; private set;
    }

    /// <summary>Gets the next allowed step.</summary>
    public TwoFactorRequiredAction RequiredAction
    {
        get; private set;
    }

    /// <summary>Gets the original issue time in UTC.</summary>
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <summary>Gets the absolute five-minute expiration in UTC.</summary>
    public DateTime ExpiresAt
    {
        get; private set;
    }

    /// <summary>Gets the time this proof was definitively consumed.</summary>
    public DateTime? ConsumedAt
    {
        get; private set;
    }

    /// <summary>Gets the time this proof was administratively or otherwise revoked.</summary>
    public DateTime? InvalidatedAt
    {
        get; private set;
    }

    /// <summary>Gets the authenticator version expected by this flow.</summary>
    public Guid? ExpectedCredentialId
    {
        get; private set;
    }

    /// <summary>Gets a hash of the security stamp bound to the first-factor proof.</summary>
    public byte[] SecurityStampHash { get; private set; } = [];

    /// <summary>Gets the persistence choice made during the first factor.</summary>
    public bool IsPersistent
    {
        get; private set;
    }

    /// <summary>Gets the proved browser session to revoke only after successful sign-in.</summary>
    public Guid? PreviousSessionId
    {
        get; private set;
    }

    /// <summary>Gets the fully authenticated session authorizing a management flow.</summary>
    public Guid? ManagementSessionId
    {
        get; private set;
    }

    /// <summary>Gets the unique upstream Google flow consumed by this challenge.</summary>
    public Guid? GoogleFlowId
    {
        get; private set;
    }

    /// <summary>Gets the encrypted, validated identity required for a deferred Google association.</summary>
    public string? ProtectedGoogleContext
    {
        get; private set;
    }

    /// <summary>Gets the unconfirmed authenticator version.</summary>
    public Guid? PendingCredentialId
    {
        get; private set;
    }

    /// <summary>Gets the encrypted candidate secret, never replacing the active factor before confirmation.</summary>
    public string? PendingProtectedSecret
    {
        get; private set;
    }

    /// <summary>Gets the recovery code exclusively reserved for this replacement.</summary>
    public Guid? ReservedRecoveryCodeId
    {
        get; private set;
    }

    /// <summary>Gets the time a factor was proved for this flow.</summary>
    public DateTime? VerifiedAt
    {
        get; private set;
    }

    /// <summary>Gets the exact session created by completion, for ambiguous-commit verification.</summary>
    public Guid? ResultSessionId
    {
        get; private set;
    }

    /// <summary>Gets the exact registered JWT identifier created by completion, never its value.</summary>
    public Guid? ResultAccessTokenId
    {
        get; private set;
    }

    /// <summary>Gets the exact last committed operation receipt, without its secret response.</summary>
    public Guid? LastOperationId
    {
        get; private set;
    }

    /// <summary>Records a non-secret receipt for independent ambiguous-commit verification.</summary>
    /// <param name="operationId">The application-generated operation identifier.</param>
    public void RecordOperation(Guid operationId)
    {
        LastOperationId = operationId;
    }

    /// <summary>Creates a first-factor-bound sign-in challenge with an immutable expiration.</summary>
    /// <param name="id">The challenge identifier.</param>
    /// <param name="memberId">The first-factor-authenticated member.</param>
    /// <param name="flowHash">The opaque proof hash.</param>
    /// <param name="credentialId">The current factor version, absent for enrollment.</param>
    /// <param name="securityStampHash">The current account security-stamp hash.</param>
    /// <param name="isPersistent">The remembered-session choice.</param>
    /// <param name="previousSessionId">The proven previous browser session.</param>
    /// <param name="now">The first-factor confirmation time in UTC.</param>
    /// <returns>The new challenge.</returns>
    public static TwoFactorChallenge CreateSignIn(
        Guid id,
        Guid memberId,
        byte[] flowHash,
        Guid? credentialId,
        byte[] securityStampHash,
        bool isPersistent,
        Guid? previousSessionId,
        DateTime now)
    {

        return new TwoFactorChallenge
        {
            Id = id,
            MemberId = memberId,
            FlowHash = flowHash,
            Purpose = TwoFactorFlowPurpose.SignIn,
            RequiredAction = credentialId is null ? TwoFactorRequiredAction.Enroll : TwoFactorRequiredAction.Verify,
            ExpectedCredentialId = credentialId,
            SecurityStampHash = securityStampHash,
            IsPersistent = isPersistent,
            PreviousSessionId = previousSessionId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(TwoFactorConstraints.ChallengeLifetimeMinutes)
        };
    }

    /// <summary>Binds an already verified management grant to one action and its authenticating session.</summary>
    /// <param name="purpose">The management operation proved by the current factor.</param>
    /// <param name="sessionId">The fully authenticated session.</param>
    /// <param name="now">The verification time in UTC.</param>
    /// <exception cref="InvalidOperationException">The operation is not a management action or the challenge is not live.</exception>
    public void AuthorizeManagement(
        TwoFactorFlowPurpose purpose,
        Guid sessionId,
        DateTime now)
    {
        EnsureLive(now);

        if (purpose is not (TwoFactorFlowPurpose.ReplaceAuthenticator or TwoFactorFlowPurpose.RegenerateRecoveryCodes))
            throw new InvalidOperationException("A management grant requires a specific management operation.");

        Purpose = purpose;
        ManagementSessionId = sessionId;
        VerifiedAt = now;
        RequiredAction = purpose == TwoFactorFlowPurpose.ReplaceAuthenticator
            ? TwoFactorRequiredAction.Replace
            : TwoFactorRequiredAction.Complete;
    }

    /// <summary>Binds the validated upstream Google proof without storing raw provider tokens.</summary>
    /// <param name="googleFlowId">The one-time Google flow identifier.</param>
    /// <param name="protectedContext">The encrypted deferred-association context.</param>
    public void BindGoogleProof(
        Guid googleFlowId,
        string protectedContext)
    {
        GoogleFlowId = googleFlowId;
        ProtectedGoogleContext = protectedContext;
    }

    /// <summary>Determines whether the original proof remains usable.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <returns>Whether this challenge is live.</returns>
    public bool IsLive(DateTime now)
    {

        return ConsumedAt is null && InvalidatedAt is null && ExpiresAt > now;
    }

    /// <summary>Stages a candidate only for an enrollment or replacement grant.</summary>
    /// <param name="credentialId">The candidate version.</param>
    /// <param name="protectedSecret">The encrypted candidate secret.</param>
    /// <param name="now">The current UTC time.</param>
    /// <exception cref="InvalidOperationException">The grant does not permit setup or is no longer live.</exception>
    public void StageAuthenticator(
        Guid credentialId,
        string protectedSecret,
        DateTime now)
    {
        EnsureLive(now);

        if (RequiredAction is not (TwoFactorRequiredAction.Enroll or TwoFactorRequiredAction.Replace))
            throw new InvalidOperationException("This grant does not permit authenticator setup.");

        PendingCredentialId = credentialId;
        PendingProtectedSecret = protectedSecret;
    }

    /// <summary>Restricts recovery to replacing the existing authenticator, without authorizing sign-in.</summary>
    /// <param name="recoveryCodeId">The exclusively reserved code.</param>
    /// <param name="now">The current UTC time.</param>
    /// <exception cref="InvalidOperationException">The grant does not permit recovery or is no longer live.</exception>
    public void BeginRecovery(
        Guid recoveryCodeId,
        DateTime now)
    {
        EnsureLive(now);

        if (RequiredAction != TwoFactorRequiredAction.Verify || Purpose != TwoFactorFlowPurpose.SignIn)
            throw new InvalidOperationException("This grant does not permit sign-in recovery.");

        ReservedRecoveryCodeId = recoveryCodeId;
        RequiredAction = TwoFactorRequiredAction.Replace;
    }

    /// <summary>Records proof of the current authenticator for final sign-in.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <exception cref="InvalidOperationException">The grant does not await verification or is no longer live.</exception>
    public void ConfirmVerification(DateTime now)
    {
        EnsureLive(now);

        if (RequiredAction != TwoFactorRequiredAction.Verify || Purpose != TwoFactorFlowPurpose.SignIn)
            throw new InvalidOperationException("This grant does not await sign-in verification.");

        VerifiedAt = now;
        RequiredAction = TwoFactorRequiredAction.Complete;
    }

    /// <summary>Records a confirmed new authenticator and erases its staged secret.</summary>
    /// <param name="now">The confirmation time in UTC.</param>
    /// <exception cref="InvalidOperationException">No candidate awaits confirmation or the grant is no longer live.</exception>
    public void ConfirmSetup(DateTime now)
    {
        EnsureLive(now);

        if (RequiredAction is not (TwoFactorRequiredAction.Enroll or TwoFactorRequiredAction.Replace) ||
            PendingCredentialId is null || PendingProtectedSecret is null)
            throw new InvalidOperationException("No authenticator setup awaits confirmation.");

        ExpectedCredentialId = PendingCredentialId;
        PendingCredentialId = null;
        PendingProtectedSecret = null;
        VerifiedAt = now;
        RequiredAction = TwoFactorRequiredAction.Complete;
    }

    /// <summary>Consumes a fully verified sign-in and retains exact non-secret commit receipts.</summary>
    /// <param name="sessionId">The committed session identifier.</param>
    /// <param name="accessTokenId">The registered access-token identifier.</param>
    /// <param name="now">The current UTC time.</param>
    /// <exception cref="InvalidOperationException">The proof is not a completed live sign-in.</exception>
    public void ConsumeSignIn(
        Guid sessionId,
        Guid accessTokenId,
        DateTime now)
    {
        EnsureLive(now);

        if (Purpose != TwoFactorFlowPurpose.SignIn || RequiredAction != TwoFactorRequiredAction.Complete || VerifiedAt is null)
            throw new InvalidOperationException("The sign-in has not completed second-factor verification.");

        ResultSessionId = sessionId;
        ResultAccessTokenId = accessTokenId;
        Consume(now);
    }

    /// <summary>Consumes a management grant without issuing a session.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <exception cref="InvalidOperationException">The proof is not a verified, completed management grant.</exception>
    public void ConsumeManagement(DateTime now)
    {
        EnsureLive(now);

        if (Purpose == TwoFactorFlowPurpose.SignIn || RequiredAction != TwoFactorRequiredAction.Complete || VerifiedAt is null)
            throw new InvalidOperationException("The management operation has not been verified and completed.");

        Consume(now);
    }

    /// <summary>Invalidates outstanding proof and discards sensitive staged material.</summary>
    /// <param name="now">The revocation time in UTC.</param>
    public void Invalidate(DateTime now)
    {
        InvalidatedAt ??= now;
        ClearStagedMaterial();
    }

    /// <summary>Rejects expired, consumed or invalidated proof before state changes.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <exception cref="InvalidOperationException">The proof is no longer live.</exception>
    private void EnsureLive(DateTime now)
    {

        if (!IsLive(now))
            throw new InvalidOperationException("The second-factor grant is no longer live.");
    }

    /// <summary>Retains the consumption receipt while removing all staged material.</summary>
    /// <param name="now">The completion time in UTC.</param>
    private void Consume(DateTime now)
    {
        ConsumedAt = now;
        ClearStagedMaterial();
    }

    /// <summary>Removes candidate credentials and deferred provider data as soon as they are unnecessary.</summary>
    private void ClearStagedMaterial()
    {
        PendingCredentialId = null;
        PendingProtectedSecret = null;
        ProtectedGoogleContext = null;
    }
}
