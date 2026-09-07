namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes the current database-backed administrator access.</summary>
public enum AdministratorAccess
{
    /// <summary>The member no longer exists.</summary>
    MemberNotFound,
    /// <summary>The member has no administrator role.</summary>
    Forbidden,
    /// <summary>The member has the administrator role.</summary>
    Granted
}
