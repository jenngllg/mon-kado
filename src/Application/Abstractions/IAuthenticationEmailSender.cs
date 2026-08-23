using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for authentication email sender.
/// </summary>

public interface IAuthenticationEmailSender
{
    /// <summary>
    /// Executes the send email confirmation async operation.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a confirmation link to a requested new member email address.
    /// </summary>
    /// <param name="message">The confirmation message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider delivery result.</returns>
    Task<AuthenticationEmailSendResult> SendEmailChangeConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a security notification to the current member email address.
    /// </summary>
    /// <param name="message">The security notification.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider delivery result.</returns>
    Task<AuthenticationEmailSendResult> SendEmailChangeSecurityNotificationAsync(
        AuthenticationEmailSecurityNotification message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a notification after a member password change.
    /// </summary>
    /// <param name="message">The password change notification.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider delivery result.</returns>
    Task<AuthenticationEmailSendResult> SendPasswordChangedSecurityNotificationAsync(
        AuthenticationPasswordChangedNotification message,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a password reset link to an eligible account email address.
    /// </summary>
    /// <param name="message">The password reset message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider delivery result.</returns>
    /// <exception cref="AuthenticationEmailDeliveryException">Thrown when the external provider rejects delivery.</exception>
    Task<AuthenticationEmailSendResult> SendPasswordResetAsync(
        AuthenticationPasswordResetMessage message,
        CancellationToken cancellationToken);
}
