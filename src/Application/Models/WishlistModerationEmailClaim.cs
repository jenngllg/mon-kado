using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains a durable outbox lease without private message content.</summary>
[ExcludeFromCodeCoverage]
public class WishlistModerationEmailClaim
{
    /// <summary>Gets the event and notification identifier.</summary>
    public Guid EventId
    {
        get; init;
    }
    /// <summary>Gets the lease fencing token.</summary>
    public Guid LeaseId
    {
        get; init;
    }
    /// <summary>Gets the claimed delivery attempt number.</summary>
    public int AttemptCount
    {
        get; init;
    }
    /// <summary>Gets the UTC lease expiration.</summary>
    public DateTime LockedUntil
    {
        get; init;
    }
}
