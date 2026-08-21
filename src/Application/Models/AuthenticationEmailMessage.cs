using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;
/// <summary>
/// Represents authentication email message.
/// </summary>
/// <param name="outboxMessageId">The outbox message id.</param>
/// <param name="recipientAddress">The recipient address.</param>
/// <param name="confirmationUrl">The confirmation url.</param>

[ExcludeFromCodeCoverage]
public class AuthenticationEmailMessage(
    Guid outboxMessageId,
    string recipientAddress,
    Uri confirmationUrl)
{
    /// <summary>
    /// Gets outbox message id.
    /// </summary>
    public Guid OutboxMessageId { get; } = outboxMessageId;
    /// <summary>
    /// Gets recipient address.
    /// </summary>

    public string RecipientAddress { get; } = recipientAddress;
    /// <summary>
    /// Gets confirmation url.
    /// </summary>

    public Uri ConfirmationUrl { get; } = confirmationUrl;
}
