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

    public int AttemptCount { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? LastError { get; private set; }

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
}
