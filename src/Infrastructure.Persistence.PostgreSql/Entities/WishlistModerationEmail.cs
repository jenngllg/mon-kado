using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Tracks one durable notification per moderation event, without copying personal data.</summary>
public class WishlistModerationEmail
{
    private WishlistModerationEmail()
    {
    }

    /// <summary>Creates the notification in the same transaction as its decision.</summary>
    /// <param name="eventId">The unique event and notification identifier.</param>
    /// <param name="createdAt">The UTC decision date.</param>
    /// <returns>The pending notification.</returns>
    public static WishlistModerationEmail Create(
        Guid eventId,
        DateTime createdAt)
    {

        return new WishlistModerationEmail
        {
            Id = eventId,
            CreatedAt = createdAt,
            AvailableAt = createdAt
        };
    }

    /// <summary>Gets the event identifier, also used as the notification identity.</summary>
    public Guid Id
    {
        get; private set;
    }
    /// <summary>Gets the UTC creation date.</summary>
    public DateTime CreatedAt
    {
        get; private set;
    }
    /// <summary>Gets the earliest UTC next-attempt date.</summary>
    public DateTime AvailableAt
    {
        get; private set;
    }
    /// <summary>Gets the number of claimed delivery attempts.</summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public int AttemptCount
    {
        get; private set;
    }
    /// <summary>Gets the lease owner token used to fence late acknowledgements.</summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public Guid? LeaseId
    {
        get; private set;
    }
    /// <summary>Gets the UTC lease expiration.</summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTime? LockedUntil
    {
        get; private set;
    }
    /// <summary>Gets the UTC terminal processing date.</summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTime? ProcessedAt
    {
        get; private set;
    }
    /// <summary>Gets the bounded technical delivery failure classification.</summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public string? LastError
    {
        get; private set;
    }
}
