using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents all gift wishes from a private wishlist.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishCollectionResponse(IReadOnlyCollection<WishCollectionItemResponse> wishes)
{
    /// <summary>
    /// Gets all gift wishes ordered by position.
    /// </summary>
    public IReadOnlyCollection<WishCollectionItemResponse> Wishes { get; } = wishes;
}
