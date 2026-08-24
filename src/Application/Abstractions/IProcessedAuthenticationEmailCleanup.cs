namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Defines cleanup operations for processed authentication emails.
/// </summary>
public interface IProcessedAuthenticationEmailCleanup
{
    /// <summary>
    /// Deletes processed authentication emails up to an inclusive cutoff.
    /// </summary>
    /// <param name="cutoff">The inclusive UTC processing cutoff.</param>
    /// <param name="batchSize">The maximum number of messages to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted messages.</returns>
    Task<int> DeleteProcessedEmailsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
