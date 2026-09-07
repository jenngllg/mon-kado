using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Persists report state and immutable review events within the scoped transaction.</summary>
public interface IWishlistReportReviewRepository
{
    /// <summary>Locks the deciding account before checking its current privileges.</summary>
    /// <param name="administratorId">The deciding account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the account still exists.</returns>
    Task<bool> LockAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken);
    /// <summary>Locks the parent before its report to serialize against cascade deletion.</summary>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the wishlist still exists.</returns>
    Task<bool> LockWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
    /// <summary>Reads and locks the tracked report under its parent.</summary>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report, or null when inaccessible.</returns>
    Task<WishlistReport?> LockReportAsync(
        Guid wishlistId,
        Guid reportId,
        CancellationToken cancellationToken);
    /// <summary>Reads detached report state under its parent.</summary>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report, or null when inaccessible.</returns>
    Task<WishlistReport?> GetReportAsync(
        Guid wishlistId,
        Guid reportId,
        CancellationToken cancellationToken);
    /// <summary>Reads the latest durable review for commit reconciliation.</summary>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The last review event, if any.</returns>
    Task<WishlistReportReviewEvent?> GetLatestEventAsync(
        Guid reportId,
        CancellationToken cancellationToken);
    /// <summary>Allocates the next sequence while the report lock is held.</summary>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The next sequence.</returns>
    Task<long> GetNextSequenceAsync(
        Guid reportId,
        CancellationToken cancellationToken);
    /// <summary>Stages an immutable review without creating a notification.</summary>
    /// <param name="review">The review to persist.</param>
    void AddEvent(WishlistReportReviewEvent review);
    /// <summary>Projects a bounded page in reverse sequence order.</summary>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="page">The requested page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching page and count.</returns>
    Task<WishlistReportReviewEventPage> GetEventsAsync(
        Guid reportId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
