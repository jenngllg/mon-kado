using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

/// <summary>
/// Defines the stable identifiers of built-in roles.
/// </summary>
[ExcludeFromCodeCoverage]
public static class RoleIds
{
    /// <summary>
    /// Identifies the built-in Member role.
    /// </summary>
    public static readonly Guid Member = new("0198d027-51c0-7000-8000-000000000002");
}
