using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Temporarily retains a protected recipient independently of the erased account.</summary>
/// <param name="id">The durable erasure event identifier.</param>
/// <param name="protectedRecipient">The operation-bound protected recipient.</param>
/// <param name="createdAt">The UTC erasure date.</param>
public class AccountErasureEmail(
    Guid id,
    string protectedRecipient,
    DateTime createdAt)
{
    /// <summary>Gets the erasure event identifier.</summary>
    public Guid Id { get; private set; } = id;
    /// <summary>Gets the encrypted recipient, removed with this row after delivery or abandonment.</summary>
    public string ProtectedRecipient { get; private set; } = protectedRecipient;
    /// <summary>Gets the UTC erasure date.</summary>
    public DateTime CreatedAt { get; private set; } = createdAt;
    /// <summary>Gets the absolute last permissible notification time.</summary>
    public DateTime ExpiresAt { get; private set; } = createdAt.AddHours(AdministrativeAccountErasureConstraints.NotificationLifetimeHours);
    /// <summary>Gets the earliest next claim date.</summary>
    public DateTime AvailableAt { get; private set; } = createdAt;
    /// <summary>Gets the number of durable attempts.</summary>
    public int AttemptCount
    {
        get; private set;
    }
    /// <summary>Gets the current fencing token.</summary>
    public Guid? LeaseId
    {
        get; private set;
    }
    /// <summary>Gets the current lease deadline.</summary>
    public DateTime? LockedUntil
    {
        get; private set;
    }
}
