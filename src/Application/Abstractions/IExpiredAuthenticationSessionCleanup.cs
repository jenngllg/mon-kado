namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for expired authentication session cleanup.
/// </summary>

public interface IExpiredAuthenticationSessionCleanup
{
    /// <summary>
    /// Executes the delete expired sessions async operation.
    /// </summary>
    /// <param name="cutoff">The cutoff.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<int> DeleteExpiredSessionsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
