namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Requests and confirms irreversible member account deletion.</summary>
public interface IMemberAccountDeletionService
{
    /// <summary>Persists a confirmation request and its delivery message.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the durable request.</returns>
    Task RequestAsync(
        Guid memberId,
        CancellationToken cancellationToken);
    /// <summary>Confirms the request and atomically removes the member's data.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="token">The protected confirmation token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the confirmed deletion.</returns>
    Task ConfirmAsync(
        Guid memberId,
        string token,
        CancellationToken cancellationToken);
}
