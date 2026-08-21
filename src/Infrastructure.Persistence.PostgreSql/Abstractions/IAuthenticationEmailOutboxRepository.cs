using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines persistence operations for authentication-email outbox messages.
/// </summary>
public interface IAuthenticationEmailOutboxRepository
{
    /// <summary>
    /// Adds an outbox message to the current unit of work.
    /// </summary>
    /// <param name="message">The message to add.</param>
    void Add(AuthenticationEmailOutboxMessage message);

    /// <summary>
    /// Gets and locks the next deliverable outbox message.
    /// </summary>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked message when one can be claimed; otherwise, <see langword="null" />.</returns>
    Task<AuthenticationEmailOutboxMessage?> GetNextForUpdateAsync(
        DateTime now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets a tracked outbox message by identifier.
    /// </summary>
    /// <param name="messageId">The message identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked message when found; otherwise, <see langword="null" />.</returns>
    Task<AuthenticationEmailOutboxMessage?> GetByIdForUpdateAsync(
        Guid messageId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks all pending confirmation messages for a user as processed.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="processedAt">The UTC processing date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task MarkPendingConfirmationMessagesProcessedAsync(
        Guid userId,
        DateTime processedAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether a pending confirmation message exists for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when a pending message exists; otherwise, <see langword="false" />.</returns>
    Task<bool> HasPendingConfirmationMessageAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets confirmation-request statistics for a user and time window.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="windowStart">The inclusive UTC beginning of the window.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The statistics when requests exist; otherwise, <see langword="null" />.</returns>
    Task<EmailRequestStatistics?> GetConfirmationRequestStatisticsAsync(
        Guid userId,
        DateTime windowStart,
        CancellationToken cancellationToken);
}
