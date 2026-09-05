using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for wishlist reports.
/// </summary>
/// <param name="context">The database context.</param>
public class WishlistReportRepository(MonKadoDbContext context) : IWishlistReportRepository
{
    /// <inheritdoc />
    public void Add(WishlistReport report)
    {
        context.WishlistReports.Add(report);
    }
}
