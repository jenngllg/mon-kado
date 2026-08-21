using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
/// <summary>
/// Represents register account request.
/// </summary>
/// <param name="email">The email.</param>
/// <param name="password">The password.</param>
/// <param name="displayName">The display name.</param>

[ExcludeFromCodeCoverage]
public class RegisterAccountRequest(
    string? email,
    string? password,
    string? displayName)
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
    /// Gets display name.
    /// </summary>

    public string? DisplayName { get; } = displayName;
}
