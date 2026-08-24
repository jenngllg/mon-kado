using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Carries the exact persistence markers produced by one Google authentication transaction attempt.
/// </summary>
/// <param name="authenticationContext">The protected Google authentication context.</param>
internal sealed class GoogleAuthenticationExecutionState(
    GoogleAuthenticationContext authenticationContext)
{
    /// <summary>
    /// Gets the protected Google authentication context.
    /// </summary>
    internal GoogleAuthenticationContext AuthenticationContext { get; } = authenticationContext;

    /// <summary>
    /// Gets the member identifier attached to the attempted refresh session.
    /// </summary>
    internal Guid? AttemptedSessionMemberId
    {
        get; private set;
    }

    /// <summary>
    /// Gets the exact refresh token created by the current transaction attempt.
    /// </summary>
    internal string? AttemptedRefreshToken
    {
        get; private set;
    }

    /// <summary>
    /// Gets whether the transaction attempt persisted a failed password check.
    /// </summary>
    internal bool PasswordFailureWasRecorded
    {
        get; private set;
    }

    /// <summary>
    /// Clears the persistence markers before an execution-strategy retry.
    /// </summary>
    internal void Reset()
    {
        AttemptedSessionMemberId = null;
        AttemptedRefreshToken = null;
        PasswordFailureWasRecorded = false;
    }

    /// <summary>
    /// Records the exact refresh session created by the current transaction attempt.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="refreshToken">The exact refresh token returned by the attempt.</param>
    internal void RecordSession(
        Guid memberId,
        string refreshToken)
    {
        AttemptedSessionMemberId = memberId;
        AttemptedRefreshToken = refreshToken;
    }

    /// <summary>
    /// Records the exact Identity mutation produced by one invalid password attempt.
    /// </summary>
    internal void RecordPasswordFailure()
    {
        PasswordFailureWasRecorded = true;
    }
}
