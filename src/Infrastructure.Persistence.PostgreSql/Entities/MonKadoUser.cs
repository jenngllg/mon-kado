using JennGllg.Fr.MonKado.Back.Domain.Abstractions;

using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
/// <summary>
/// Represents mon kado user.
/// </summary>

public class MonKadoUser : IdentityUser<Guid>, IAuditableEntity
{
    /// <summary>
    /// Gets display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Gets created at.
    /// </summary>

    public DateTime CreatedAt
    {
        get; private set;
    }
    /// <summary>
    /// Gets updated at.
    /// </summary>

    public DateTime? UpdatedAt
    {
        get; private set;
    }
    /// <summary>
    /// Gets unconfirmed account expires at.
    /// </summary>

    public DateTime? UnconfirmedAccountExpiresAt
    {
        get; set;
    }
    /// <summary>
    /// Gets version.
    /// </summary>

    public uint Version
    {
        get; private set;
    }
}
