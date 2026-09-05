using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Defines durable cleanup coordination for replaced gift images.
/// </summary>
public interface IGiftImageCleanupService
{
    /// <summary>
    /// Claims the next available deletion with a renewable lease.
    /// </summary>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="leaseDuration">The claim lease duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The claimed deletion, or <see langword="null" /> when none is available.</returns>
    Task<GiftImageDeletion?> ClaimNextAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a successfully processed deletion from the queue.
    /// </summary>
    /// <param name="deletionId">The deletion identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task CompleteAsync(
        Guid deletionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reschedules a failed deletion.
    /// </summary>
    /// <param name="deletionId">The deletion identifier.</param>
    /// <param name="availableAt">The next UTC attempt date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ScheduleRetryAsync(
        Guid deletionId,
        DateTime availableAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether an image is currently referenced by a gift wish.
    /// </summary>
    /// <param name="imageId">The image identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the image is referenced.</returns>
    Task<bool> IsReferencedAsync(
        Guid imageId,
        CancellationToken cancellationToken);
}
