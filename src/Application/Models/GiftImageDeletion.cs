using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents one claimed gift-image deletion.
/// </summary>
/// <param name="id">The outbox message identifier.</param>
/// <param name="imageId">The image identifier.</param>
/// <param name="attemptCount">The current delivery attempt count.</param>
[ExcludeFromCodeCoverage]
public class GiftImageDeletion(
    Guid id,
    Guid imageId,
    int attemptCount)
{
    /// <summary>
    /// Gets the outbox message identifier.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Gets the image identifier.
    /// </summary>
    public Guid ImageId { get; } = imageId;

    /// <summary>
    /// Gets the current attempt count.
    /// </summary>
    public int AttemptCount { get; } = attemptCount;
}
