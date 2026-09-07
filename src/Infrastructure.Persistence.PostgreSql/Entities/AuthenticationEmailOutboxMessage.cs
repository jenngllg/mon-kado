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
    /// Gets the related member email change request identifier.
    /// </summary>
    public Guid? MemberEmailChangeRequestId
    {
        get; private set;
    }
    /// <summary>Gets the deletion request identity, retained after supersession for delivery filtering.</summary>
    public Guid? MemberAccountDeletionRequestId
    {
        get; private set;
    }
    /// <summary>Gets the export whose availability is rechecked before notification delivery.</summary>
    public Guid? MemberDataExportId
    {
        get; private set;
    }

    /// <summary>Creates one notification without snapshotting an email address or including a download secret.</summary>
    /// <param name="exportId">The published export identifier.</param>
    /// <param name="memberId">The current export owner.</param>
    /// <param name="readyAt">The UTC publication date.</param>
    /// <returns>The unique export-ready notification.</returns>
    public static AuthenticationEmailOutboxMessage CreatePersonalDataExportReady(
        Guid exportId,
        Guid memberId,
        DateTime readyAt)
    {

        return new AuthenticationEmailOutboxMessage
        {
            Id = Guid.CreateVersion7(),
            UserId = memberId,
            MemberDataExportId = exportId,
            Kind = AuthenticationEmailKind.PersonalDataExportReady,
            CreatedAt = readyAt,
            AvailableAt = readyAt
        };
    }

    /// <summary>Creates a confirmation message bound to a durable account deletion request.</summary>
    /// <param name="request">The request containing the immutable recipient security state.</param>
    /// <returns>The new delivery message.</returns>
    public static AuthenticationEmailOutboxMessage CreateAccountDeletionConfirmation(MemberAccountDeletionRequest request)
    {

        return new AuthenticationEmailOutboxMessage
        {
            Id = Guid.CreateVersion7(),
            UserId = request.MemberId,
            MemberAccountDeletionRequestId = request.Id,
            RecipientEmail = request.Email,
            SecurityStampSnapshot = request.SecurityStamp,
            Kind = AuthenticationEmailKind.AccountDeletionConfirmation,
            CreatedAt = request.CreatedAt,
            AvailableAt = request.CreatedAt
        };
    }

    /// <summary>
    /// Gets the immutable recipient address for request-specific messages.
    /// </summary>
    public string? RecipientEmail
    {
        get; private set;
    }
    /// <summary>
    /// Gets the immutable security stamp used to validate a password reset or email change confirmation.
    /// </summary>
    public string? SecurityStampSnapshot
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
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public int AttemptCount
    {
        get; private set;
    }

    /// <summary>
    /// Gets locked until.
    /// </summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTime? LockedUntil
    {
        get; private set;
    }

    /// <summary>
    /// Gets processed at.
    /// </summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public DateTime? ProcessedAt
    {
        get; private set;
    }

    /// <summary>
    /// Gets last error.
    /// </summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
    public string? LastError
    {
        get; private set;
    }

    /// <summary>
    /// Gets provider message id.
    /// </summary>
    [SuppressMessage("CodeQuality", "S1144:Unused private types or members should be removed", Justification = "Entity Framework uses this private setter when materializing persisted outbox state.")]
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

    /// <summary>
    /// Creates a confirmation message for a requested member email change.
    /// </summary>
    /// <param name="requestId">The member email change request identifier.</param>
    /// <param name="userId">The member identifier.</param>
    /// <param name="recipientEmail">The new email address.</param>
    /// <param name="securityStampSnapshot">The member security stamp at request time.</param>
    /// <param name="createdAt">The creation date and time.</param>
    /// <returns>The created outbox message.</returns>
    public static AuthenticationEmailOutboxMessage CreateEmailChangeConfirmation(
        Guid requestId,
        Guid userId,
        string recipientEmail,
        string securityStampSnapshot,
        DateTime createdAt)
    {

        return CreateEmailChangeMessage(
            requestId,
            userId,
            recipientEmail,
            securityStampSnapshot,
            AuthenticationEmailKind.EmailChangeConfirmation,
            createdAt);
    }

    /// <summary>
    /// Creates a security notification for a requested member email change.
    /// </summary>
    /// <param name="requestId">The member email change request identifier.</param>
    /// <param name="userId">The member identifier.</param>
    /// <param name="recipientEmail">The current email address.</param>
    /// <param name="createdAt">The creation date and time.</param>
    /// <returns>The created outbox message.</returns>
    public static AuthenticationEmailOutboxMessage CreateEmailChangeSecurityNotification(
        Guid requestId,
        Guid userId,
        string recipientEmail,
        DateTime createdAt)
    {

        return CreateEmailChangeMessage(
            requestId,
            userId,
            recipientEmail,
            null,
            AuthenticationEmailKind.EmailChangeSecurityNotification,
            createdAt);
    }

    /// <summary>
    /// Creates a security notification for a completed member password change.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="recipientEmail">The current member email address.</param>
    /// <param name="createdAt">The password change date and time.</param>
    /// <returns>The created outbox message.</returns>
    public static AuthenticationEmailOutboxMessage CreatePasswordChangedSecurityNotification(
        Guid userId,
        string recipientEmail,
        DateTime createdAt)
    {

        return new AuthenticationEmailOutboxMessage
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(createdAt)),
            UserId = userId,
            RecipientEmail = recipientEmail,
            Kind = AuthenticationEmailKind.PasswordChangedSecurityNotification,
            CreatedAt = createdAt,
            AvailableAt = createdAt
        };
    }

    /// <summary>
    /// Creates a password reset message for an eligible member.
    /// </summary>
    /// <param name="userId">The member identifier.</param>
    /// <param name="recipientEmail">The member email address at request time.</param>
    /// <param name="securityStampSnapshot">The member security stamp at request time.</param>
    /// <param name="createdAt">The request date and time.</param>
    /// <returns>The created outbox message.</returns>
    public static AuthenticationEmailOutboxMessage CreatePasswordReset(
        Guid userId,
        string recipientEmail,
        string securityStampSnapshot,
        DateTime createdAt)
    {

        return new AuthenticationEmailOutboxMessage
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(createdAt)),
            UserId = userId,
            RecipientEmail = recipientEmail,
            SecurityStampSnapshot = securityStampSnapshot,
            Kind = AuthenticationEmailKind.PasswordReset,
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

    internal void MarkFailed(
        DateTime failedAt,
        string lastError)
    {
        ProcessedAt = failedAt;
        LockedUntil = null;
        LastError = lastError;
        ProviderMessageId = null;
    }

    private static AuthenticationEmailOutboxMessage CreateEmailChangeMessage(
        Guid requestId,
        Guid userId,
        string recipientEmail,
        string? securityStampSnapshot,
        AuthenticationEmailKind kind,
        DateTime createdAt)
    {

        return new AuthenticationEmailOutboxMessage
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(createdAt)),
            UserId = userId,
            MemberEmailChangeRequestId = requestId,
            RecipientEmail = recipientEmail,
            SecurityStampSnapshot = securityStampSnapshot,
            Kind = kind,
            CreatedAt = createdAt,
            AvailableAt = createdAt
        };
    }
}
