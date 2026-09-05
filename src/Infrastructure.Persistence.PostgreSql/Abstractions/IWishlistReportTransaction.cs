namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Represents one PostgreSQL transaction used to create a wishlist report.
/// </summary>
public interface IWishlistReportTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commits the transaction.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous commit.</returns>
    Task CommitAsync(CancellationToken cancellationToken);
}
