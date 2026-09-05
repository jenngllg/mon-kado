using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Persists anonymous reports submitted through active wishlist share links.
/// </summary>
public interface IWishlistReportService
{
    /// <summary>
    /// Verifies a share link and creates an anonymous report for its wishlist.
    /// </summary>
    /// <param name="reportId">The generated report identifier.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="shareSecret">The presented share-link secret.</param>
    /// <param name="reason">The report reason.</param>
    /// <param name="details">The optional normalized details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reported wishlist identifier.</returns>
    /// <exception cref="SharedWishlistNotFoundException">The share link or secret is invalid.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<Guid> CreateAsync(
        Guid reportId,
        Guid shareLinkId,
        string shareSecret,
        WishlistReportReason reason,
        string? details,
        CancellationToken cancellationToken);
}
