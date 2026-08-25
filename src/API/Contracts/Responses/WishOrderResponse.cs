using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents the complete lightweight order of a gift wish collection.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishOrderResponse(IReadOnlyCollection<WishOrderItemResponse> wishes)
{
    /// <summary>
    /// Gets the complete ordered collection.
    /// </summary>
    public IReadOnlyCollection<WishOrderItemResponse> Wishes { get; } = wishes;
}
