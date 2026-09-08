using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Retains minimal accountability independently of the revoked sessions.</summary>
[ExcludeFromCodeCoverage]
public class AdministrativeSessionRevocationEvent
{
    /// <summary>Gets the identifier of this exact operation.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the actor, cleared if the account is erased.</summary>
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>Gets the target, cleared if the account is erased.</summary>
    public Guid? MemberId
    {
        get; init;
    }
    /// <summary>Gets the normalized technical reference, never logged.</summary>
    public string RequestReference { get; init; } = string.Empty;
    /// <summary>Gets the UTC revocation time.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
}
