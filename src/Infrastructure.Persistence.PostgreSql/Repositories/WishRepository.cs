using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for gift wishes.
/// </summary>
/// <param name="context">The database context.</param>
public class WishRepository(MonKadoDbContext context) : IWishRepository
{
    private const string AllocatePositionSql = """
        INSERT INTO public.wish_position_sequences (wishlist_id, next_position, current_count)
        VALUES (@wishlistId, 1, 0)
        ON CONFLICT (wishlist_id)
        DO UPDATE SET next_position = public.wish_position_sequences.next_position + 1
        RETURNING next_position;
        """;

    /// <inheritdoc />
    public async Task<long> AllocatePositionAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var mustCloseConnection = connection.State is not ConnectionState.Open;

        if (mustCloseConnection)
            await context.Database.OpenConnectionAsync(cancellationToken);

        long position;

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = AllocatePositionSql;
            command.Parameters.AddWithValue(
                "wishlistId",
                NpgsqlDbType.Uuid,
                wishlistId);
            var value = await command.ExecuteScalarAsync(cancellationToken);

            position = Convert.ToInt64(
                value,
                System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (mustCloseConnection)
                await context.Database.CloseConnectionAsync();
        }

        return position;
    }

    /// <inheritdoc />
    public void Add(Wish wish)
    {
        context.Wishes.Add(wish);
    }

    /// <inheritdoc />
    public void Remove(Wish wish)
    {
        context.Wishes.Remove(wish);
    }

    /// <inheritdoc />
    public Task<Wish?> GetByIdAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        return context.Wishes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                wish => wish.WishlistId == wishlistId && wish.Id == wishId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<Wish?> GetByIdForUpdateAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        return context.Wishes
            .SingleOrDefaultAsync(
                wish => wish.WishlistId == wishlistId && wish.Id == wishId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Wish>> GetByWishlistIdAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return await context.Wishes
            .AsNoTracking()
            .Where(wish => wish.WishlistId == wishlistId)
            .OrderBy(wish => wish.Position)
            .ThenBy(wish => wish.Id)
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Wish>> GetByWishlistIdForUpdateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return await context.Wishes
            .FromSqlInterpolated($"""
                SELECT wish.*, wish.xmin
                FROM public.wishes AS wish
                WHERE wish.wishlist_id = {wishlistId}
                ORDER BY wish.position, wish.id
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishPositionSequence?> GetCollectionStateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return context.WishPositionSequences
            .AsNoTracking()
            .SingleOrDefaultAsync(
                sequence => sequence.WishlistId == wishlistId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishPositionSequence?> GetCollectionStateForUpdateAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        return context.WishPositionSequences
            .FromSqlInterpolated($"""
                SELECT sequence.*, sequence.xmin
                FROM public.wish_position_sequences AS sequence
                WHERE sequence.wishlist_id = {wishlistId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task ReloadCollectionStateAsync(
        WishPositionSequence sequence,
        CancellationToken cancellationToken)
    {
        return context.Entry(sequence)
            .ReloadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void ClearTracking()
    {
        context.ChangeTracker.Clear();
    }
}
