using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>Retains a private administrator decision throughout the wishlist lifetime.</summary>
public class WishlistModerationEvent
{
    private WishlistModerationEvent()
    {
    }

    /// <summary>Initializes an immutable moderation decision.</summary>
    /// <param name="id">The application-generated event identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="administratorId">The deciding administrator identifier.</param>
    /// <param name="sequence">The strictly increasing wishlist decision sequence.</param>
    /// <param name="action">The recorded decision.</param>
    /// <param name="reason">The private suspension reason, if applicable.</param>
    /// <param name="occurredAt">The UTC decision date.</param>
    public WishlistModerationEvent(
        Guid id,
        Guid wishlistId,
        Guid administratorId,
        long sequence,
        WishlistModerationAction action,
        string? reason,
        DateTime occurredAt)
    {
        Id = id;
        WishlistId = wishlistId;
        AdministratorId = administratorId;
        Sequence = sequence;
        Action = action;
        Reason = reason;
        OccurredAt = occurredAt;
    }

    /// <summary>Gets the event identifier.</summary>
    public Guid Id
    {
        get; private set;
    }
    /// <summary>Gets the wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; private set;
    }
    /// <summary>Gets the administrator identifier, or null after account deletion.</summary>
    public Guid? AdministratorId
    {
        get; private set;
    }
    /// <summary>Gets the durable ordering within the wishlist.</summary>
    public long Sequence
    {
        get; private set;
    }
    /// <summary>Gets the decision.</summary>
    public WishlistModerationAction Action
    {
        get; private set;
    }
    /// <summary>Gets the private suspension reason.</summary>
    public string? Reason
    {
        get; private set;
    }
    /// <summary>Gets the UTC decision date.</summary>
    public DateTime OccurredAt
    {
        get; private set;
    }
}
