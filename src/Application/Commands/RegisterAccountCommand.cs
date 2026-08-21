using MediatR;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;
/// <summary>
/// Represents register account command.
/// </summary>
/// <param name="email">The email.</param>
/// <param name="password">The password.</param>
/// <param name="displayName">The display name.</param>

[ExcludeFromCodeCoverage]
public class RegisterAccountCommand(
    string? email,
    string? password,
    string? displayName) : IRequest
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
