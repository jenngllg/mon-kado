using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents a member password change security notification.
/// </summary>
/// <param name="outboxMessageId">The outbox message identifier.</param>
/// <param name="recipientAddress">The member email address.</param>
/// <param name="changedAt">The password change date and time.</param>
[ExcludeFromCodeCoverage]
public class AuthenticationPasswordChangedNotification(
    Guid outboxMessageId,
    string recipientAddress,
    DateTime changedAt)
{
    /// <summary>
    /// Gets the outbox message identifier.
    /// </summary>
    public Guid OutboxMessageId { get; } = outboxMessageId;

    /// <summary>
    /// Gets the notification recipient address.
    /// </summary>
    public string RecipientAddress { get; } = recipientAddress;

    /// <summary>
    /// Gets the password change date and time.
    /// </summary>
    public DateTime ChangedAt { get; } = changedAt;
}
