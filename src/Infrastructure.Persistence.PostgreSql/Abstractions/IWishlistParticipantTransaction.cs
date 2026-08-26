namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Represents one PostgreSQL transaction used by participant operations.
/// </summary>
public interface IWishlistParticipantTransaction : IAsyncDisposable
{
    /// <summary>Commits the transaction.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous commit.</returns>
    Task CommitAsync(CancellationToken cancellationToken);
}
