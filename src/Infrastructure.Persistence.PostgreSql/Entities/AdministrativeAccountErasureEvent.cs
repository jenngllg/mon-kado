using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Retains minimal accountability without requiring the erased account to exist.</summary>
/// <param name="administratorId">The acting administrator.</param>
/// <param name="memberId">The pseudonymous target identifier retained for the audit lifetime.</param>
/// <param name="requestReference">The external reference without personal content.</param>
/// <param name="createdAt">The UTC erasure date.</param>
/// <param name="notificationStatus">The initial notification state.</param>
public class AdministrativeAccountErasureEvent(
    Guid? administratorId,
    Guid memberId,
    string requestReference,
    DateTime createdAt,
    AccountErasureNotificationStatus notificationStatus)
{
    /// <summary>Gets the durable erasure operation identifier.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    /// <summary>Gets the actor, detached if that account is subsequently erased.</summary>
    public Guid? AdministratorId { get; private set; } = administratorId;
    /// <summary>Gets the erased account identifier without a foreign key to its deleted row.</summary>
    public Guid MemberId { get; private set; } = memberId;
    /// <summary>Gets the external reference, never included in logs.</summary>
    public string RequestReference { get; private set; } = requestReference;
    /// <summary>Gets the UTC erasure date.</summary>
    public DateTime CreatedAt { get; private set; } = createdAt;
    /// <summary>Gets the bounded notification outcome.</summary>
    public AccountErasureNotificationStatus NotificationStatus { get; private set; } = notificationStatus;
}
