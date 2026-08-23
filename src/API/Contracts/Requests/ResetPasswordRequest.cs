using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents an anonymous password reset request.
/// </summary>
/// <param name="userId">The account identifier from the reset link.</param>
/// <param name="token">The password reset token.</param>
/// <param name="newPassword">The new account password.</param>
[ExcludeFromCodeCoverage]
public class ResetPasswordRequest(
    string? userId,
    string? token,
    string? newPassword)
{
    /// <summary>
    /// Gets the account identifier from the reset link.
    /// </summary>
    public string? UserId { get; } = userId;

    /// <summary>
    /// Gets the password reset token.
    /// </summary>
    public string? Token { get; } = token;

    /// <summary>
    /// Gets the new account password.
    /// </summary>
    public string? NewPassword { get; } = newPassword;
}
