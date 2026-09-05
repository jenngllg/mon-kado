using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents an image whose database commit still needs reconciliation.
/// </summary>
/// <param name="imageId">The image identifier.</param>
/// <param name="createdAt">The UTC marker creation date and time.</param>
[ExcludeFromCodeCoverage]
public class PendingGiftImage(
    Guid imageId,
    DateTime createdAt)
{
    /// <summary>
    /// Gets the image identifier.
    /// </summary>
    public Guid ImageId { get; } = imageId;

    /// <summary>
    /// Gets the UTC marker creation date and time.
    /// </summary>
    public DateTime CreatedAt { get; } = createdAt;
}
