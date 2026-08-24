namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Carries the exact persistence markers produced by one refresh-session transaction attempt.
/// </summary>
/// <param name="sessionId">The refresh session identifier being rotated.</param>
internal sealed class RefreshSessionExecutionState(Guid sessionId)
{
    /// <summary>
    /// Gets the refresh session identifier being rotated.
    /// </summary>
    internal Guid SessionId { get; } = sessionId;

    /// <summary>
    /// Gets the member identifier attached to the attempted rotation.
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
    /// Gets whether the attempted rotation was persistent.
    /// </summary>
    internal bool? AttemptedIsPersistent
    {
        get; private set;
    }

    /// <summary>
    /// Gets whether the transaction attempt persisted a terminal session revocation.
    /// </summary>
    internal bool RevocationWasRecorded
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
        AttemptedIsPersistent = null;
        RevocationWasRecorded = false;
    }

    /// <summary>
    /// Records the exact refresh-session rotation produced by the current transaction attempt.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="refreshToken">The exact refresh token returned by the attempt.</param>
    /// <param name="isPersistent">Whether the rotated session is persistent.</param>
    internal void RecordRotation(
        Guid memberId,
        string refreshToken,
        bool isPersistent)
    {
        AttemptedSessionMemberId = memberId;
        AttemptedRefreshToken = refreshToken;
        AttemptedIsPersistent = isPersistent;
    }

    /// <summary>
    /// Records that the transaction attempt reached its terminal revocation boundary.
    /// </summary>
    internal void RecordRevocation()
    {
        RevocationWasRecorded = true;
    }
}
