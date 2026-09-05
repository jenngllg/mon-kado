using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Retrieves durable reservation history for authenticated members.
/// </summary>
public interface IGiftReservationHistoryService
{
    /// <summary>
    /// Gets one page of a member's reservation history.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="status">The optional lifecycle status filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The history page, or <see langword="null" /> when the member no longer exists.</returns>
    Task<GiftReservationHistoryPage?> GetAsync(
        Guid memberId,
        int page,
        int pageSize,
        GiftReservationHistoryStatus? status,
        CancellationToken cancellationToken);
}
