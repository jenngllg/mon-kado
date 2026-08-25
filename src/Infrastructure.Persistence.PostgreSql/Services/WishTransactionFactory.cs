using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates EF Core transactions for gift wish collection operations.
/// </summary>
/// <param name="context">The database context.</param>
public class WishTransactionFactory(MonKadoDbContext context) : IWishTransactionFactory
{
    /// <inheritdoc />
    public async Task<IWishTransaction> BeginAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(
            isolationLevel,
            cancellationToken);

        return new WishTransaction(transaction);
    }
}
