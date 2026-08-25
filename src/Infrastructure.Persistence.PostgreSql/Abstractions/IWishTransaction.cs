namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Represents one PostgreSQL transaction used by gift wish collection operations.
/// </summary>
public interface IWishTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous commit.</returns>
    Task CommitAsync(CancellationToken cancellationToken);
}
