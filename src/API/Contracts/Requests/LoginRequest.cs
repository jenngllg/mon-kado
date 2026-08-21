using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
/// <summary>
/// Represents login request.
/// </summary>
/// <param name="email">The email.</param>
/// <param name="password">The password.</param>
/// <param name="rememberMe">The remember me.</param>

[ExcludeFromCodeCoverage]
public class LoginRequest(
    string? email,
    string? password,
    bool rememberMe = false)
{
    /// <summary>
    /// Gets email.
    /// </summary>
    public string? Email { get; } = email;
    /// <summary>
    /// Gets password.
    /// </summary>

    public string? Password { get; } = password;
    /// <summary>
    /// Gets remember me.
    /// </summary>

    public bool RememberMe { get; } = rememberMe;
}
