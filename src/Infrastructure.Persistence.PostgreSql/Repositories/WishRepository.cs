using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

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
        INSERT INTO public.wish_position_sequences (wishlist_id, next_position)
        VALUES (@wishlistId, 1)
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
}
