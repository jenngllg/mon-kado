using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Rejects every authentication email delivery attempted by an integration scenario.
/// </summary>
public class UnexpectedAuthenticationEmailSender : IAuthenticationEmailSender
{
    /// <inheritdoc />
    public Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return CreateFailure();
    }

    /// <inheritdoc />
    public Task<AuthenticationEmailSendResult> SendEmailChangeConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return CreateFailure();
    }

    /// <inheritdoc />
    public Task<AuthenticationEmailSendResult> SendEmailChangeSecurityNotificationAsync(
        AuthenticationEmailSecurityNotification message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return CreateFailure();
    }

    /// <inheritdoc />
    public Task<AuthenticationEmailSendResult> SendPasswordChangedSecurityNotificationAsync(
        AuthenticationPasswordChangedNotification message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return CreateFailure();
    }

    /// <inheritdoc />
    public Task<AuthenticationEmailSendResult> SendPasswordResetAsync(
        AuthenticationPasswordResetMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return CreateFailure();
    }

    /// <summary>
    /// Creates the failure returned for an unexpected delivery.
    /// </summary>
    /// <returns>The failed delivery task.</returns>
    private static Task<AuthenticationEmailSendResult> CreateFailure()
    {

        return Task.FromException<AuthenticationEmailSendResult>(
            new InvalidOperationException("No authentication email delivery was expected."));
    }
}
