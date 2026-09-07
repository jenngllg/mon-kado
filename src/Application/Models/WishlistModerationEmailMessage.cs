using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains private moderation notification data for the current confirmed owner.</summary>
[ExcludeFromCodeCoverage]
public class WishlistModerationEmailMessage
{
    /// <summary>Gets the immutable event identifier used as Message-ID.</summary>
    public Guid EventId
    {
        get; init;
    }
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }
    /// <summary>Gets the current wishlist name; never log this value.</summary>
    public string WishlistName { get; init; } = string.Empty;
    /// <summary>Gets the current confirmed recipient address; never log this value.</summary>
    public string RecipientAddress { get; init; } = string.Empty;
    /// <summary>Gets the historical decision.</summary>
    public WishlistModerationAction Action
    {
        get; init;
    }
    /// <summary>Gets the private reason applicable to this decision; never log this value.</summary>
    public string? Reason
    {
        get; init;
    }
    /// <summary>Gets the UTC historical decision date.</summary>
    public DateTime OccurredAt
    {
        get; init;
    }
}
