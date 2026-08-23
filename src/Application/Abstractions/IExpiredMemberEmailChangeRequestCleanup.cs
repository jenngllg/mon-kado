namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Defines cleanup operations for expired member email change requests.
/// </summary>
public interface IExpiredMemberEmailChangeRequestCleanup
{
    /// <summary>
    /// Deletes expired or retained completed member email change requests.
    /// </summary>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="batchSize">The maximum batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted requests.</returns>
    Task<int> DeleteExpiredRequestsAsync(
        DateTime now,
        int batchSize,
        CancellationToken cancellationToken);
}
