using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates EF Core transactions and locks for wishlist report creation.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="shareLinkRepository">The parent-first shared resource lock repository.</param>
public class WishlistReportTransactionFactory(
    MonKadoDbContext context,
    IWishlistShareLinkRepository shareLinkRepository)
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

        return shareLinkRepository.LockActiveAsync(
            shareLinkId,
            cancellationToken);
    }
}
