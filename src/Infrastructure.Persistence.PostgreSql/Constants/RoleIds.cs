using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

/// <summary>
/// Defines the stable identifiers of built-in roles.
/// </summary>
[ExcludeFromCodeCoverage]
public static class RoleIds
{
    /// <summary>Identifies the built-in Admin role without assigning it to any account.</summary>
    public static readonly Guid Admin = new("019ec170-1570-7000-8000-000000000001");

    /// <summary>
    /// Identifies the built-in Member role.
    /// </summary>
    public static readonly Guid Member = new("0198d027-51c0-7000-8000-000000000002");
}
