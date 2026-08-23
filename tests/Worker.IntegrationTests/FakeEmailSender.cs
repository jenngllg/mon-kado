using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

internal class FakeEmailSender(
    bool fail = false,
    TimeSpan? delay = null,
    TimeSpan? retryAfter = null,
    AuthenticationEmailFailureCategory failureCategory = AuthenticationEmailFailureCategory.Transient)
    : IAuthenticationEmailSender
{
    public ConcurrentQueue<AuthenticationEmailMessage> Messages { get; } = new();

    public ConcurrentQueue<AuthenticationPasswordResetMessage> PasswordResetMessages { get; } = new();

    public ConcurrentQueue<AuthenticationEmailMessage> EmailChangeConfirmations { get; } = new();

    public ConcurrentQueue<AuthenticationEmailSecurityNotification> EmailChangeNotifications { get; } = new();

    public ConcurrentQueue<AuthenticationPasswordChangedNotification> PasswordChangedNotifications { get; } = new();

    public async Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {
        Messages.Enqueue(message);

        if (delay is { } value)
            await Task.Delay(
                value,
                cancellationToken);

        return fail
            ? throw new AuthenticationEmailDeliveryException(
                failureCategory,
                retryAfter)
            : new AuthenticationEmailSendResult("fake-provider-id");
    }

    public async Task<AuthenticationEmailSendResult> SendPasswordResetAsync(
        AuthenticationPasswordResetMessage message,
        CancellationToken cancellationToken)
    {
        PasswordResetMessages.Enqueue(message);

        return await CompleteAsync(cancellationToken);
    }

    public async Task<AuthenticationEmailSendResult> SendEmailChangeConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {
        EmailChangeConfirmations.Enqueue(message);

        return await CompleteAsync(cancellationToken);
    }

    public async Task<AuthenticationEmailSendResult> SendEmailChangeSecurityNotificationAsync(
        AuthenticationEmailSecurityNotification message,
        CancellationToken cancellationToken)
    {
        EmailChangeNotifications.Enqueue(message);

        return await CompleteAsync(cancellationToken);
    }

    public async Task<AuthenticationEmailSendResult> SendPasswordChangedSecurityNotificationAsync(
        AuthenticationPasswordChangedNotification message,
        CancellationToken cancellationToken)
    {
        PasswordChangedNotifications.Enqueue(message);

        return await CompleteAsync(cancellationToken);
    }

    private async Task<AuthenticationEmailSendResult> CompleteAsync(
        CancellationToken cancellationToken)
    {

        if (delay is { } value)
            await Task.Delay(
                value,
                cancellationToken);

        return fail
            ? throw new AuthenticationEmailDeliveryException(
                failureCategory,
                retryAfter)
            : new AuthenticationEmailSendResult("fake-provider-id");
    }
}
