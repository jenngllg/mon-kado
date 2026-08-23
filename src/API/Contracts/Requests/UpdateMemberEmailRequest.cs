using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a request to update the current member email address.
/// </summary>
/// <param name="email">The requested email address.</param>
/// <param name="currentPassword">The current member password.</param>
[ExcludeFromCodeCoverage]
public class UpdateMemberEmailRequest(
    string? email,
    string? currentPassword)
{
    /// <summary>
    /// Gets the requested email address.
    /// </summary>
    public string? Email { get; } = email;

    /// <summary>
    /// Gets the current member password.
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;
}
