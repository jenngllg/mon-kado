using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains only delivery metadata for a second-factor security notification.</summary>
/// <param name="outboxMessageId">The durable notification identifier.</param>
/// <param name="recipientAddress">The account address at the time of the operation.</param>
/// <param name="securityEvent">The bounded security event.</param>
/// <param name="createdAt">The UTC operation timestamp.</param>
[ExcludeFromCodeCoverage]
public class TwoFactorSecurityNotification(
    Guid outboxMessageId,
    string recipientAddress,
    TwoFactorSecurityEvent securityEvent,
    DateTime createdAt)
{
    /// <summary>Gets the durable notification identifier.</summary>
    public Guid OutboxMessageId { get; } = outboxMessageId;
    /// <summary>Gets the recipient address.</summary>
    public string RecipientAddress { get; } = recipientAddress;
    /// <summary>Gets the completed security event.</summary>
    public TwoFactorSecurityEvent SecurityEvent { get; } = securityEvent;
    /// <summary>Gets the UTC operation timestamp.</summary>
    public DateTime CreatedAt { get; } = createdAt;
}
