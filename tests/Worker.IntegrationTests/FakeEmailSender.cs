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
}
