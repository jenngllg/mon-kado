using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a password reset email request.
/// </summary>
/// <param name="email">The account email address.</param>
[ExcludeFromCodeCoverage]
public class RequestPasswordResetRequest(string? email)
{
    /// <summary>
    /// Gets the account email address.
    /// </summary>
    public string? Email { get; } = email;
}
