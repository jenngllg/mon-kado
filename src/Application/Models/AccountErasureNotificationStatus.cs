namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes the bounded result of the post-erasure notification.</summary>
public enum AccountErasureNotificationStatus
{
    /// <summary>No confirmed address was available.</summary>
    NotApplicable,
    /// <summary>The durable notification is pending.</summary>
    Pending,
    /// <summary>The provider accepted the message; receipt is not guaranteed.</summary>
    Accepted,
    /// <summary>Delivery was abandoned and the recipient was discarded.</summary>
    Failed
}
