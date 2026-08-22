using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the current authenticated member session.
/// </summary>
/// <param name="id">The member identifier.</param>
/// <param name="email">The member email address.</param>
/// <param name="displayName">The member display name.</param>
/// <param name="roles">The current member roles.</param>
/// <param name="version">The member profile version.</param>
[ExcludeFromCodeCoverage]
public class CurrentSession(
    Guid id,
    string email,
    string displayName,
    IEnumerable<string> roles,
    uint version)
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

    /// <summary>
    /// Gets the member profile version used for optimistic concurrency.
    /// </summary>
    public uint Version { get; } = version;
}
