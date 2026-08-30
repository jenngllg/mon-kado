using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore.Storage;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Adapts an EF Core transaction for gift reservation operations.
/// </summary>
/// <param name="transaction">The EF Core transaction.</param>
public class GiftReservationTransaction(IDbContextTransaction transaction) : IGiftReservationTransaction
{
    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
