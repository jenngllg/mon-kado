using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates EF Core transactions and locks for wishlist report creation.
/// </summary>
/// <param name="context">The database context.</param>
public class WishlistReportTransactionFactory(MonKadoDbContext context)
    : IWishlistReportTransactionFactory
{
    /// <inheritdoc />
    public async Task<IWishlistReportTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        return new WishlistReportTransaction(transaction);
    }

    /// <inheritdoc />
    public Task<WishlistShareLink?> LockShareLinkAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {

        return context.WishlistShareLinks
            .FromSqlInterpolated($"SELECT *, xmin FROM public.wishlist_share_links WHERE id = {shareLinkId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }
}
