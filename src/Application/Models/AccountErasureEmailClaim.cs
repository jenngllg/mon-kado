using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes one fenced attempt without exposing a plaintext recipient.</summary>
[ExcludeFromCodeCoverage]
public class AccountErasureEmailClaim
{
    /// <summary>Gets the erasure event identifier.</summary>
    public Guid OperationId
    {
        get; init;
    }
    /// <summary>Gets the lease fencing token.</summary>
    public Guid LeaseId
    {
        get; init;
    }
    /// <summary>Gets the durable attempt number.</summary>
    public int AttemptCount
    {
        get; init;
    }
    /// <summary>Gets the lease deadline, bounded by recipient expiration.</summary>
    public DateTime LockedUntil
    {
        get; init;
    }
    /// <summary>Gets the absolute recipient expiration.</summary>
    public DateTime ExpiresAt
    {
        get; init;
    }
    /// <summary>Gets the operation-bound encrypted recipient.</summary>
    public string ProtectedRecipient { get; init; } = string.Empty;
    /// <summary>Gets the UTC erasure date.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
}
