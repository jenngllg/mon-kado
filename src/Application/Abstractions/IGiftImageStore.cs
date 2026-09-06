using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Defines durable local storage operations for normalized gift images.
/// </summary>
public interface IGiftImageStore
{
    /// <summary>
    /// Writes a new immutable image and its pending-commit marker atomically.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <param name="content">The normalized WebP bytes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task WritePendingAsync(
        Guid imageId,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the pending marker after the database commit is confirmed.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task MarkCommittedAsync(
        Guid imageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens an immutable image for reading.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The readable stream, or <see langword="null" /> when the file is absent.</returns>
    Task<Stream?> OpenReadAsync(
        Guid imageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes an image and any pending marker idempotently.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeleteAsync(
        Guid imageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets pending image markers up to an inclusive UTC cutoff.
    /// </summary>
    /// <param name="cutoff">The inclusive UTC marker cutoff.</param>
    /// <param name="batchSize">The maximum number of markers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pending images ordered by marker age.</returns>
    Task<IReadOnlyCollection<PendingGiftImage>> GetPendingAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken);
}
