using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Copies authorized shared content and independently owned image files atomically.</summary>
/// <param name="context">The scoped persistence context.</param>
/// <param name="transactionFactory">The transaction factory.</param>
/// <param name="mutationGuard">The owned destination guard.</param>
/// <param name="links">The bearer-link repository.</param>
/// <param name="tokens">The share-secret verifier.</param>
/// <param name="wishes">The wish repository.</param>
/// <param name="unitOfWork">The shared unit of work.</param>
/// <param name="images">The immutable image store.</param>
/// <param name="logger">The structured logger.</param>
[SuppressMessage(
    "CodeQuality",
    "S107:Methods should not have too many parameters",
    Justification = "The transaction explicitly composes the existing access, persistence and image storage boundaries.")]
public class WishCopyService(
    MonKadoDbContext context,
    IWishTransactionFactory transactionFactory,
    IWishlistMutationGuard mutationGuard,
    IWishlistShareLinkRepository links,
    IWishlistShareTokenService tokens,
    IWishRepository wishes,
    IUnitOfWork unitOfWork,
    IGiftImageStore images,
    ILogger<WishCopyService> logger) : IWishCopyService
{
    private const string WishCountConstraintName = "ck_wish_position_sequences_current_count_limit";
    private const int ImageCopyBufferLength = 64 * 1024;

    /// <inheritdoc />
    public async Task<WishDetails> CopyAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        Guid sourceShareLinkId,
        Guid sourceWishId,
        string secret,
        CancellationToken cancellationToken)
    {
        WishDetails result;
        try
        {
            await using var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var initialLink = await links.GetByIdAsync(
                sourceShareLinkId,
                cancellationToken);

            if (initialLink is null)
                throw new SharedWishlistNotFoundException();

            // Keep the existing account-before-parent order, then order both parents identically for cross-copies.
            var owners = await context.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM public.users WHERE id = {ownerId} FOR KEY SHARE
                """)
                .ToArrayAsync(cancellationToken);

            if (owners.Length == 0)
                throw new InvalidAuthenticationSessionException();

            await context.Wishlists
                .FromSqlInterpolated($"""
                    SELECT wishlist.*, wishlist.xmin FROM public.wishlists AS wishlist
                    WHERE wishlist.id = {wishlistId} OR wishlist.id = {initialLink.WishlistId}
                    ORDER BY wishlist.id FOR UPDATE
                    """)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken);
            await mutationGuard.LockAsync(
                ownerId,
                wishlistId,
                cancellationToken);
            var link = await links.LockActiveAsync(
                sourceShareLinkId,
                cancellationToken);

            if (link is null || !tokens.Verify(
                secret,
                link.SecretHash))
                throw new SharedWishlistNotFoundException();

            var source = await wishes.GetByIdAsync(
                link.WishlistId,
                sourceWishId,
                cancellationToken);

            if (source is null)
                throw new SharedWishNotFoundException();

            var position = await wishes.AllocatePositionAsync(
                wishlistId,
                cancellationToken);
            var wish = new Wish(
                id,
                wishlistId,
                source.Name,
                source.Note,
                source.Url,
                source.Price,
                position,
                source.Quantity);
            await CopyImageAsync(
                source,
                wish,
                cancellationToken);
            wishes.Add(wish);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            result = new WishDetails(
                wish.Id,
                wish.WishlistId,
                wish.Name,
                wish.Note,
                wish.Url,
                wish.Price,
                wish.Position,
                wish.CreatedAt,
                wish.UpdatedAt,
                wish.Version,
                wish.Quantity,
                wish.ImageId);
        }
        catch (DbUpdateException exception) when (exception.InnerException is
            PostgresException { SqlState: PostgresErrorCodes.CheckViolation, ConstraintName: WishCountConstraintName })
        {
            throw new WishLimitReachedException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            // Never replay a possibly committed copy. The client directs uncertain outcomes to a fresh list read.
            throw new DependencyUnavailableException(
                DependencyNames.PostgreSql,
                exception);
        }

        if (result.ImageId is Guid imageId)
        {
            try
            {
                await images.MarkCommittedAsync(
                    imageId,
                    cancellationToken);
            }
            catch (GiftImageStorageUnavailableException)
            {
                ApplicationLogMessages.GiftImagePendingCleanupFailed(
                    logger,
                    imageId);
            }
        }

        return result;
    }

    /// <summary>Copies bounded normalized bytes, verifying their stored integrity hash.</summary>
    /// <param name="source">The locked source snapshot.</param>
    /// <param name="destination">The new independent wish.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the pending file is durable.</returns>
    /// <exception cref="GiftImageStorageUnavailableException">The normalized source file is unavailable or corrupt.</exception>
    /// <exception cref="OperationCanceledException">The operation is cancelled.</exception>
    private async Task CopyImageAsync(
        Wish source,
        Wish destination,
        CancellationToken cancellationToken)
    {
        if (source.ImageId is not Guid sourceImageId)
            return;

        await using var input = await images.OpenReadAsync(
            sourceImageId,
            cancellationToken);

        if (input is null)
            throw new GiftImageStorageUnavailableException(new IOException("The source image is unavailable."));

        using var output = new MemoryStream();
        var buffer = new byte[ImageCopyBufferLength];
        int count;

        try
        {
            while ((count = await input.ReadAsync(
                buffer,
                cancellationToken)) != 0)
            {
                if (output.Length + count > GiftImageConstraints.MaximumInputLength)
                    throw new GiftImageStorageUnavailableException(new IOException("The source image exceeds its storage limit."));

                output.Write(
                    buffer,
                    0,
                    count);
            }
        }
        catch (IOException exception)
        {
            throw new GiftImageStorageUnavailableException(exception);
        }

        var bytes = output.ToArray();
        var hash = SHA256.HashData(bytes);

        if (!source.HasImageContentHash(hash))
            throw new GiftImageStorageUnavailableException(new IOException("The source image failed its integrity check."));

        var imageId = Guid.CreateVersion7();
        await images.WritePendingAsync(
            imageId,
            bytes,
            cancellationToken);
        destination.ReplaceImage(
            imageId,
            hash);
    }
}
