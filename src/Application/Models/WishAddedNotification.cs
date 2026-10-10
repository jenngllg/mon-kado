using MediatR;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Identifies a committed new wish for a future preference-aware notification consumer.
/// This integration event has no delivery consumer or retained backlog in #998.</summary>
/// <param name="wishlistId">The destination wishlist.</param>
/// <param name="wishId">The new wish.</param>
/// <param name="occurredAt">The UTC creation time.</param>
[ExcludeFromCodeCoverage]
public class WishAddedNotification(
    Guid wishlistId,
    Guid wishId,
    DateTime occurredAt) : INotification
{
    /// <summary>Gets the destination list identifier.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the new wish identifier.</summary>
    public Guid WishId { get; } = wishId;
    /// <summary>Gets the UTC creation time.</summary>
    public DateTime OccurredAt { get; } = occurredAt;
}
