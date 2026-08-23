using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a current member password update request.
/// </summary>
/// <param name="currentPassword">The current member password.</param>
/// <param name="newPassword">The new member password.</param>
[ExcludeFromCodeCoverage]
public class UpdateMemberPasswordRequest(
    string? currentPassword,
    string? newPassword)
{
    /// <summary>
    /// Gets the current member password.
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;

    /// <summary>
    /// Gets the new member password.
    /// </summary>
    public string? NewPassword { get; } = newPassword;
}
