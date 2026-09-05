using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL persistence operations for wishlist reports.
/// </summary>
public interface IWishlistReportRepository
{
    /// <summary>
    /// Adds an anonymous wishlist report to the current unit of work.
    /// </summary>
    /// <param name="report">The report.</param>
    void Add(WishlistReport report);
}
