using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads and updates private report reviews without moderating the wishlist.</summary>
public interface IWishlistReportReviewService
{
    /// <summary>Reads the current report and its optimistic concurrency version.</summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current report.</returns>
    /// <exception cref="WishlistReportNotFoundException">The report does not belong to the requested wishlist.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<VersionedWishlistReportDetails> GetAsync(
        Guid wishlistId,
        Guid reportId,
        CancellationToken cancellationToken);
    /// <summary>Applies and records a validated administrative review atomically.</summary>
    /// <param name="administratorId">The deciding administrator.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="status">The requested disposition.</param>
    /// <param name="note">The optional private note.</param>
    /// <param name="expectedVersion">The expected report version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated report.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The administrator account no longer exists.</exception>
    /// <exception cref="AdministratorAccessDeniedException">The account no longer has administrator privileges.</exception>
    /// <exception cref="WishlistReportNotFoundException">The report does not belong to the requested wishlist.</exception>
    /// <exception cref="WishlistReportVersionConflictException">The report version has changed.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable or the commit cannot be confirmed.</exception>
    Task<VersionedWishlistReportDetails> UpdateAsync(
        Guid administratorId,
        Guid wishlistId,
        Guid reportId,
        WishlistReportStatus status,
        string? note,
        uint expectedVersion,
        CancellationToken cancellationToken);
    /// <summary>Reads a stable page of private review history.</summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <param name="page">The requested one-based page.</param>
    /// <param name="pageSize">The page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested history page.</returns>
    /// <exception cref="WishlistReportNotFoundException">The report does not belong to the requested wishlist.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<WishlistReportReviewEventPage> GetEventsAsync(
        Guid wishlistId,
        Guid reportId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
