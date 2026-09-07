using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates EF Core transactions and locks for wishlist participant operations.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="shareLinkRepository">The parent-first shared resource lock repository.</param>
public class WishlistParticipantTransactionFactory(
    MonKadoDbContext context,
    IWishlistShareLinkRepository shareLinkRepository)
    : IWishlistParticipantTransactionFactory
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
    public async Task<IWishlistParticipantTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        return new WishlistParticipantTransaction(transaction);
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
    public async Task<Guid> LockWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var wishlist = await context.Wishlists
            .FromSqlInterpolated($"SELECT *, xmin FROM public.wishlists WHERE id = {wishlistId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        return wishlist.OwnerId;
    }
}
