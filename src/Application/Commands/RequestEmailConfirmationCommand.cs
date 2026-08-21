using MediatR;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;
/// <summary>
/// Represents request email confirmation command.
/// </summary>
/// <param name="email">The email.</param>

[ExcludeFromCodeCoverage]
public class RequestEmailConfirmationCommand(string? email) : IRequest
{
    /// <summary>
    /// Gets email.
    /// </summary>
    public string? Email { get; } = email;
}
