using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads reported wishlist content for database-authorized administrators.</summary>
public interface IReportedWishlistService
{
    /// <summary>Reads grouped reports using one repeatable-read snapshot.</summary>
    /// <param name="reason">The reason.</param>
    /// <param name="isSuspended">The isSuspended.</param>
    /// <param name="page">The page.</param>
    /// <param name="pageSize">The pageSize.</param>
    /// <param name="cancellationToken">The cancellationToken.</param>
    /// <returns>The requested read-only result.</returns>
    Task<ReportedWishlistPage> GetPageAsync(
        WishlistReportReason? reason,
        bool? isSuspended,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    /// <summary>Reads the current content of a reported wishlist.</summary>
    /// <param name="wishlistId">The wishlistId.</param>
    /// <param name="cancellationToken">The cancellationToken.</param>
    /// <returns>The requested read-only result.</returns>
    Task<ReportedWishlistDetails> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken);
    /// <summary>Reads anonymous reports using one repeatable-read snapshot.</summary>
    /// <param name="wishlistId">The wishlistId.</param>
    /// <param name="reason">The reason.</param>
    /// <param name="page">The page.</param>
    /// <param name="pageSize">The pageSize.</param>
    /// <param name="cancellationToken">The cancellationToken.</param>
    /// <returns>The requested read-only result.</returns>
    Task<WishlistReportPage> GetReportsAsync(
        Guid wishlistId,
        WishlistReportReason? reason,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    /// <summary>Opens the currently referenced image of a reported wishlist.</summary>
    /// <param name="wishlistId">The wishlistId.</param>
    /// <param name="wishId">The wishId.</param>
    /// <param name="cancellationToken">The cancellationToken.</param>
    /// <returns>The requested read-only result.</returns>
    Task<Stream> OpenImageAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken);
}
