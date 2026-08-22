using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for authentication email outbox messages.
/// </summary>
/// <param name="context">The database context.</param>
public class AuthenticationEmailOutboxRepository(MonKadoDbContext context)
    : IAuthenticationEmailOutboxRepository
{
    /// <inheritdoc />
    public void Add(AuthenticationEmailOutboxMessage message)
    {
        context.AuthenticationEmailOutboxMessages.Add(message);
    }

    /// <inheritdoc />
    public Task<AuthenticationEmailOutboxMessage?> GetNextForUpdateAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {

        return context.AuthenticationEmailOutboxMessages
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM public.authentication_email_outbox
                WHERE processed_at IS NULL
                  AND available_at <= {now}
                  AND (locked_until IS NULL OR locked_until <= {now})
                ORDER BY available_at, created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuthenticationEmailOutboxMessage?> GetByIdForUpdateAsync(
        Guid messageId,
        CancellationToken cancellationToken)
    {

        return context.AuthenticationEmailOutboxMessages.SingleOrDefaultAsync(
            message => message.Id == messageId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkPendingConfirmationMessagesProcessedAsync(
        Guid userId,
        DateTime processedAt,
        CancellationToken cancellationToken)
    {
        await context.AuthenticationEmailOutboxMessages
            .Where(message =>
                message.UserId == userId &&
                message.Kind == AuthenticationEmailKind.EmailConfirmation &&
                message.ProcessedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        message => message.ProcessedAt,
                        processedAt)
                    .SetProperty(
                        message => message.LockedUntil,
                        (DateTime?)null),
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> HasPendingConfirmationMessageAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {

        return context.AuthenticationEmailOutboxMessages.AnyAsync(
            message =>
                message.UserId == userId &&
                message.Kind == AuthenticationEmailKind.EmailConfirmation &&
                message.ProcessedAt == null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmailRequestStatistics?> GetConfirmationRequestStatisticsAsync(
        Guid userId,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {

        return context.AuthenticationEmailOutboxMessages
            .Where(message =>
                message.UserId == userId &&
                message.Kind == AuthenticationEmailKind.EmailConfirmation &&
                message.CreatedAt >= windowStart)
            .GroupBy(_ => 1)
            .Select(group => new EmailRequestStatistics(
                group.Count(),
                group.Max(message => message.CreatedAt)))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
