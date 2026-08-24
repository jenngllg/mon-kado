namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Carries the exact persistence markers produced by one password-login transaction attempt.
/// </summary>
/// <param name="sessionId">The stable session identifier reserved for the logical login operation.</param>
internal sealed class AccountLoginExecutionState(Guid sessionId)
{
    /// <summary>
    /// Gets the stable session identifier reserved for the logical login operation.
    /// </summary>
    internal Guid SessionId { get; private set; } = sessionId;

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
    /// Clears the attempt-specific persistence markers before an execution-strategy retry.
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
    /// Records that one failed password attempt reached its persistence boundary.
    /// </summary>
    internal void RecordPasswordFailure()
    {
        PasswordFailureWasRecorded = true;
    }

    /// <summary>
    /// Reserves another session identifier when the exact committed session became unusable before verification.
    /// </summary>
    /// <param name="sessionId">The replacement session identifier.</param>
    internal void PrepareSessionRetry(Guid sessionId)
    {
        SessionId = sessionId;
    }
}
