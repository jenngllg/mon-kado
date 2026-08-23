using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents a password reset email message.
/// </summary>
/// <param name="outboxMessageId">The outbox message identifier.</param>
/// <param name="recipientAddress">The recipient email address.</param>
/// <param name="resetUrl">The password reset URL.</param>
[ExcludeFromCodeCoverage]
public class AuthenticationPasswordResetMessage(
    Guid outboxMessageId,
    string recipientAddress,
    Uri resetUrl)
{
    /// <summary>
    /// Gets the outbox message identifier.
    /// </summary>
    public Guid OutboxMessageId { get; } = outboxMessageId;

    /// <summary>
    /// Gets the recipient email address.
    /// </summary>
    public string RecipientAddress { get; } = recipientAddress;

    /// <summary>
    /// Gets the password reset URL.
    /// </summary>
    public Uri ResetUrl { get; } = resetUrl;
}
