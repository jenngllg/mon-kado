namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Deletes expired guest-session credentials while retaining participation history.
/// </summary>
public interface IExpiredGuestSessionCleanup
{
    /// <summary>Deletes one batch of expired guest sessions.</summary>
    /// <param name="batchSize">The maximum number of sessions to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted sessions.</returns>
    Task<int> DeleteExpiredSessionsAsync(
        int batchSize,
        CancellationToken cancellationToken);
}
