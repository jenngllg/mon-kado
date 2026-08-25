using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents one gift wish inside a complete order response.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishOrderItemResponse(
    Guid id,
    long position,
    string entityTag)
{
    /// <summary>
    /// Gets the wish identifier.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Gets the position inside the parent wishlist.
    /// </summary>
    public long Position { get; } = position;

    /// <summary>
    /// Gets the individual strong entity tag.
    /// </summary>
    public string EntityTag { get; } = entityTag;
}
