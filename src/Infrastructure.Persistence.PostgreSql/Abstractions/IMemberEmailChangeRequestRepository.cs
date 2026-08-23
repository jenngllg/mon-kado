using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines persistence operations for member email change requests.
/// </summary>
public interface IMemberEmailChangeRequestRepository
{
    /// <summary>
    /// Adds a request to the current unit of work.
    /// </summary>
    /// <param name="request">The request to add.</param>
    void Add(MemberEmailChangeRequest request);

    /// <summary>
    /// Gets and locks an active request for a member.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active request when found.</returns>
    Task<MemberEmailChangeRequest?> GetActiveByUserIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets and locks a request by identifier.
    /// </summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request when found.</returns>
    Task<MemberEmailChangeRequest?> GetByIdForUpdateAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets an untracked request by identifier.
    /// </summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request when found.</returns>
    Task<MemberEmailChangeRequest?> GetByIdAsync(
        Guid requestId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one batch of completed or expired requests.
    /// </summary>
    /// <param name="expirationCutoff">The inclusive request expiration cutoff.</param>
    /// <param name="completedCutoff">The inclusive completed-request retention cutoff.</param>
    /// <param name="batchSize">The maximum batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted requests.</returns>
    Task<int> DeleteExpiredOrCompletedAsync(
        DateTime expirationCutoff,
        DateTime completedCutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
