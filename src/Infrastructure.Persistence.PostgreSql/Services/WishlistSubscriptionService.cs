using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Creates revocable subscriptions under the existing parent and share-link locks.</summary>
/// <param name="repository">The scoped subscription repository.</param>
/// <param name="shareLinks">The share-link repository.</param>
/// <param name="wishlists">The wishlist repository.</param>
/// <param name="tokens">The share-token verifier.</param>
/// <param name="transactions">The transaction factory.</param>
/// <param name="unitOfWork">The scoped unit of work.</param>
public class WishlistSubscriptionService(
    IWishlistSubscriptionRepository repository,
    IWishlistShareLinkRepository shareLinks,
    IWishlistRepository wishlists,
    IWishlistShareTokenService tokens,
    IWishTransactionFactory transactions,
    IUnitOfWork unitOfWork) : IWishlistSubscriptionService
{
    private const string PostgreSqlDependencyName = "PostgreSQL";

    /// <inheritdoc />
    public async Task<WishlistSubscriptionDetails> CreateAsync(
        Guid memberId,
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        WishlistSubscriptionDetails result;

        try
        {
            await using var transaction = await transactions.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var link = await shareLinks.LockActiveAsync(
                shareLinkId,
                cancellationToken) ?? throw new SharedWishlistNotFoundException();
            VerifySecret(
                link,
                secret);
            var wishlist = await wishlists.GetByIdAsync(
                link.WishlistId,
                cancellationToken) ?? throw new SharedWishlistNotFoundException();

            if (wishlist.OwnerId == memberId)
                throw new WishlistSubscriptionSelfException();

            var existing = await repository.GetCurrentAsync(
                memberId,
                shareLinkId,
                cancellationToken);

            if (existing is not null)
                throw new WishlistSubscriptionAlreadyExistsException();

            var subscription = new WishlistSubscription(
                Guid.CreateVersion7(),
                memberId,
                link.WishlistId,
                link.Id,
                link.SecretHash);
            repository.Add(subscription);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            result = await repository.GetAsync(
                memberId,
                subscription.Id,
                cancellationToken) ?? throw new WishlistSubscriptionNotFoundException();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_wishlist_subscriptions_member_wishlist" })
        {

            throw new WishlistSubscriptionAlreadyExistsException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<WishlistSubscriptionDetails?> GetAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {

            return await repository.GetAsync(
                memberId,
                id,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<WishlistSubscriptionDetails?> GetCurrentAsync(
        Guid memberId,
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        WishlistSubscriptionDetails? result;

        try
        {
            await using var transaction = await transactions.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var link = await shareLinks.LockActiveAsync(
                shareLinkId,
                cancellationToken) ?? throw new SharedWishlistNotFoundException();
            VerifySecret(
                link,
                secret);
            result = await repository.GetCurrentAsync(
                memberId,
                shareLinkId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<WishlistSubscriptionPage> GetPageAsync(
        Guid memberId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {

            return await repository.GetPageAsync(
                memberId,
                page,
                pageSize,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await repository.GetForUpdateAsync(
                memberId,
                id,
                cancellationToken);

            if (subscription is null)
                return false;

            repository.Remove(subscription);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {

            return false;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }
    }

    /// <summary>Checks the secret against the locked link without disclosing invalid capabilities.</summary>
    /// <param name="link">The locked active link.</param>
    /// <param name="secret">The presented secret.</param>
    /// <exception cref="SharedWishlistNotFoundException">The secret does not match.</exception>
    private void VerifySecret(
        WishlistShareLink link,
        string secret)
    {

        if (!tokens.Verify(
            secret,
            link.SecretHash))
            throw new SharedWishlistNotFoundException();
    }
}
