using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents the current authenticated member session response.
/// </summary>
/// <param name="id">The member identifier.</param>
/// <param name="email">The member email address.</param>
/// <param name="displayName">The member display name.</param>
/// <param name="roles">The current member roles.</param>
[ExcludeFromCodeCoverage]
public class CurrentSessionResponse(
    Guid id,
    string email,
    string displayName,
    IEnumerable<string> roles)
{
    /// <summary>
    /// Gets the member identifier.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Gets the member email address.
    /// </summary>
    public string Email { get; } = email;

    /// <summary>
    /// Gets the member display name.
    /// </summary>
    public string DisplayName { get; } = displayName;

    /// <summary>
    /// Gets the current member roles.
    /// </summary>
    public IEnumerable<string> Roles { get; } = Array.AsReadOnly<string>([.. roles]);
}
