using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

public sealed class AuthenticationEmailOutboxMessage
{
    private AuthenticationEmailOutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public AuthenticationEmailKind Kind { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset AvailableAt { get; private set; }

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public int AttemptCount { get; private set; }

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTimeOffset? LockedUntil { get; private set; }

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTimeOffset? ProcessedAt { get; private set; }

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public string? LastError { get; private set; }

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public string? ProviderMessageId { get; private set; }

    public static AuthenticationEmailOutboxMessage CreateEmailConfirmation(
        Guid userId,
        DateTimeOffset createdAt)
    {
        return new AuthenticationEmailOutboxMessage
        {
            Id = Guid.CreateVersion7(createdAt),
            UserId = userId,
            Kind = AuthenticationEmailKind.EmailConfirmation,
            CreatedAt = createdAt,
            AvailableAt = createdAt
        };
    }

    internal void Claim(DateTimeOffset lockedUntil)
    {
        AttemptCount++;
        LockedUntil = lockedUntil;
    }

    internal void MarkProcessed(DateTimeOffset processedAt, string? providerMessageId = null)
    {
        ProcessedAt = processedAt;
        LockedUntil = null;
        LastError = null;
        ProviderMessageId = providerMessageId;
    }

    internal void ScheduleRetry(DateTimeOffset availableAt, string lastError)
    {
        AvailableAt = availableAt;
        LockedUntil = null;
        LastError = lastError;
    }
}
