using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents a member email change security notification.
/// </summary>
/// <param name="outboxMessageId">The outbox message identifier.</param>
/// <param name="recipientAddress">The current member email address.</param>
/// <param name="requestedAddress">The requested new email address.</param>
[ExcludeFromCodeCoverage]
public class AuthenticationEmailSecurityNotification(
    Guid outboxMessageId,
    string recipientAddress,
    string requestedAddress)
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
    /// Gets the requested new email address.
    /// </summary>
    public string RequestedAddress { get; } = requestedAddress;
}
