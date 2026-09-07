using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Collections.Concurrent;

namespace JennGllg.Fr.MonKado.Back.Tests.Common;

/// <summary>Captures moderation deliveries without calling Gmail.</summary>
public class FakeWishlistModerationEmailSender : IWishlistModerationEmailSender
{
    /// <summary>Gets the messages acknowledged by this fake.</summary>
    public ConcurrentQueue<WishlistModerationEmailMessage> Messages { get; } = new();
    /// <summary>Gets or sets the optional deterministic provider behavior.</summary>
    public Func<WishlistModerationEmailMessage, CancellationToken, Task>? BeforeSendAsync
    {
        get; set;
    }

    /// <inheritdoc/>
    public async Task SendAsync(
        WishlistModerationEmailMessage message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (BeforeSendAsync is { } beforeSend)
            await beforeSend(
                message,
                cancellationToken);
        Messages.Enqueue(message);
    }
}
