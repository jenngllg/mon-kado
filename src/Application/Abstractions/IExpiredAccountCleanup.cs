namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for expired account cleanup.
/// </summary>

public interface IExpiredAccountCleanup
{
    /// <summary>
    /// Executes the delete expired unconfirmed accounts async operation.
    /// </summary>
    /// <param name="cutoff">The cutoff.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<int> DeleteExpiredUnconfirmedAccountsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
