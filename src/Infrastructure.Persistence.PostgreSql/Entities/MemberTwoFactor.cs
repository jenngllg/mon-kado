using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Retains encrypted authenticator material and account-wide verification defenses.</summary>
public class MemberTwoFactor
{
    /// <summary>Gets the credential owner.</summary>
    public Guid MemberId
    {
        get; private set;
    }

    /// <summary>Gets the current credential version, absent before enrollment.</summary>
    public Guid? CredentialId
    {
        get; private set;
    }

    /// <summary>Gets the purpose-bound encrypted secret, never its plaintext.</summary>
    public string? ProtectedSecret
    {
        get; private set;
    }

    /// <summary>Gets the date of the latest confirmed enrollment or replacement.</summary>
    public DateTime? EnabledAt
    {
        get; private set;
    }

    /// <summary>Gets the highest accepted TOTP interval for this credential.</summary>
    public long? LastAcceptedTimeStep
    {
        get; private set;
    }

    /// <summary>Gets consecutive failed second-factor verifications across challenges.</summary>
    public int FailedAttempts
    {
        get; private set;
    }

    /// <summary>Gets the end of the account-wide second-factor lockout.</summary>
    public DateTime? LockedUntil
    {
        get; private set;
    }

    /// <summary>Gets the start of the account-wide verification rate window.</summary>
    public DateTime? VerificationWindowStartedAt
    {
        get; private set;
    }

    /// <summary>Gets the number of verifications accepted in the current rate window.</summary>
    public int VerificationCount
    {
        get; private set;
    }

    /// <summary>Creates security state without enabling an authenticator.</summary>
    /// <param name="memberId">The credential owner.</param>
    /// <returns>The initial security state.</returns>
    public static MemberTwoFactor Create(Guid memberId)
    {

        return new MemberTwoFactor { MemberId = memberId };
    }

    /// <summary>Reserves an account-wide verification attempt under the member lock.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <returns>Whether verification may proceed.</returns>
    public bool TryStartVerification(DateTime now)
    {

        if (LockedUntil > now)
            return false;

        if (LockedUntil is not null)
        {
            LockedUntil = null;
            FailedAttempts = 0;
        }

        if (VerificationWindowStartedAt is null || now >= VerificationWindowStartedAt.Value.AddMinutes(1))
        {
            VerificationWindowStartedAt = now;
            VerificationCount = 0;
        }

        if (VerificationCount >= TwoFactorConstraints.VerificationsPerMinute)
            return false;

        VerificationCount++;

        return true;
    }

    /// <summary>Persists a failure without allowing a new first-factor flow to reset it.</summary>
    /// <param name="now">The failure time in UTC.</param>
    public void RecordFailure(DateTime now)
    {
        FailedAttempts++;

        if (FailedAttempts >= TwoFactorConstraints.MaximumFailedAttempts)
            LockedUntil = now.AddMinutes(TwoFactorConstraints.LockoutMinutes);
    }

    /// <summary>Consumes a verified TOTP interval exactly once under the member lock.</summary>
    /// <param name="timeStep">The interval authenticated by the cryptographic verifier.</param>
    /// <exception cref="InvalidOperationException">The interval was already accepted.</exception>
    public void AcceptTimeStep(long timeStep)
    {

        if (LastAcceptedTimeStep >= timeStep)
            throw new InvalidOperationException("The authenticator interval was already consumed.");

        LastAcceptedTimeStep = timeStep;
        FailedAttempts = 0;
        LockedUntil = null;
    }

    /// <summary>Atomically replaces the credential after the new authenticator has been proved.</summary>
    /// <param name="credentialId">The newly generated credential version.</param>
    /// <param name="protectedSecret">The encrypted new secret.</param>
    /// <param name="timeStep">The interval consumed during confirmation.</param>
    /// <param name="now">The confirmation time in UTC.</param>
    public void ConfirmAuthenticator(
        Guid credentialId,
        string protectedSecret,
        long timeStep,
        DateTime now)
    {
        CredentialId = credentialId;
        ProtectedSecret = protectedSecret;
        EnabledAt = now;
        LastAcceptedTimeStep = timeStep;
        FailedAttempts = 0;
        LockedUntil = null;
    }
}
