using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Identifies the destination for an independently copied owned wish.</summary>
/// <param name="destinationWishlistId">The nullable destination list identifier.</param>
[ExcludeFromCodeCoverage]
public class CopyOwnedWishRequest(Guid? destinationWishlistId)
{
    /// <summary>Gets the destination list identifier.</summary>
    public Guid? DestinationWishlistId { get; } = destinationWishlistId;
}
