using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
/// <summary>
/// Represents authentication email outbox message.
/// </summary>

public class AuthenticationEmailOutboxMessage
{
    private AuthenticationEmailOutboxMessage()
    {
    }
    /// <summary>
    /// Gets id.
    /// </summary>

    public Guid Id
    {
        get; private set;
    }
    /// <summary>
    /// Gets user id.
    /// </summary>

    public Guid UserId
    {
        get; private set;
    }
    /// <summary>
    /// Gets kind.
    /// </summary>

    public AuthenticationEmailKind Kind
    {
        get; private set;
    }
    /// <summary>
    /// Gets created at.
    /// </summary>

    public DateTime CreatedAt
    {
        get; private set;
    }
    /// <summary>
    /// Gets available at.
    /// </summary>

    public DateTime AvailableAt
    {
        get; private set;
    }
    /// <summary>
    /// Gets attempt count.
    /// </summary>

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public int AttemptCount
    {
        get; private set;
    }
    /// <summary>
    /// Gets locked until.
    /// </summary>

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTime? LockedUntil
    {
        get; private set;
    }
    /// <summary>
    /// Gets processed at.
    /// </summary>

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTime? ProcessedAt
    {
        get; private set;
    }
    /// <summary>
    /// Gets last error.
    /// </summary>

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public string? LastError
    {
        get; private set;
    }
    /// <summary>
    /// Gets provider message id.
    /// </summary>

    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public string? ProviderMessageId
    {
        get; private set;
    }
    /// <summary>
    /// Executes the create email confirmation operation.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="createdAt">The created at.</param>
    /// <returns>The operation result.</returns>

    public static AuthenticationEmailOutboxMessage CreateEmailConfirmation(
        Guid userId,
        DateTime createdAt)
    {

        return new AuthenticationEmailOutboxMessage
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(createdAt)),
            UserId = userId,
            Kind = AuthenticationEmailKind.EmailConfirmation,
            CreatedAt = createdAt,
            AvailableAt = createdAt
        };
    }

    internal void Claim(DateTime lockedUntil)
    {
        AttemptCount++;
        LockedUntil = lockedUntil;
    }

    internal void MarkProcessed(
        DateTime processedAt,
        string? providerMessageId = null)
    {
        ProcessedAt = processedAt;
        LockedUntil = null;
        LastError = null;
        ProviderMessageId = providerMessageId;
    }

    internal void ScheduleRetry(
        DateTime availableAt,
        string lastError)
    {
        AvailableAt = availableAt;
        LockedUntil = null;
        LastError = lastError;
    }
}
