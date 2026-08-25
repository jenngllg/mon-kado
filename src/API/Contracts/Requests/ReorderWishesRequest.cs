using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents the complete requested order of a gift wish collection.
/// </summary>
/// <param name="wishIds">All current wish identifiers in their requested final order.</param>
[ExcludeFromCodeCoverage]
public class ReorderWishesRequest(IReadOnlyCollection<Guid>? wishIds)
{
    /// <summary>
    /// Gets all current wish identifiers in their requested final order.
    /// </summary>
    public IReadOnlyCollection<Guid>? WishIds { get; } = wishIds;
}
