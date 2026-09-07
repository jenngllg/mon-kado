using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates EF Core transactions and row locks for reservation mutations.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="shareLinkRepository">The parent-first shared resource lock repository.</param>
public class GiftReservationTransactionFactory(
    MonKadoDbContext context,
    IWishlistShareLinkRepository shareLinkRepository)
    : IGiftReservationTransactionFactory
{
    /// <inheritdoc />
    public async Task LockMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var members = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM public.users WHERE id = {memberId} FOR KEY SHARE
            """)
            .ToArrayAsync(cancellationToken);

        if (members.Length == 0)
            throw new InvalidAuthenticationSessionException();
    }

    /// <inheritdoc />
    public async Task<IGiftReservationTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        return new GiftReservationTransaction(transaction);
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

    /// <inheritdoc />
    public Task<Wish?> LockWishAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        return context.Wishes
            .FromSqlInterpolated($"""
                SELECT wish.*, wish.xmin
                FROM public.wishes AS wish
                WHERE wish.wishlist_id = {wishlistId} AND wish.id = {wishId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
