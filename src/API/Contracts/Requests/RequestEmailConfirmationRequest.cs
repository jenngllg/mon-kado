using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
/// <summary>
/// Represents request email confirmation request.
/// </summary>
/// <param name="email">The email.</param>

[ExcludeFromCodeCoverage]
public class RequestEmailConfirmationRequest(string? email)
{
    /// <summary>
    /// Gets email.
    /// </summary>
    public string? Email { get; } = email;
}
