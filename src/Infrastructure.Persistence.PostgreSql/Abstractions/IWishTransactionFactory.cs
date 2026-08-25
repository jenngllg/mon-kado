using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Creates PostgreSQL transactions used by gift wish collection operations.
/// </summary>
public interface IWishTransactionFactory
{
    /// <summary>
    /// Begins a transaction with the requested isolation level.
    /// </summary>
    /// <param name="isolationLevel">The transaction isolation level.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The started transaction.</returns>
    Task<IWishTransaction> BeginAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken);
}
