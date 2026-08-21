using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
/// <summary>
/// Represents confirm email request.
/// </summary>
/// <param name="userId">The user id.</param>
/// <param name="token">The token.</param>

[ExcludeFromCodeCoverage]
public class ConfirmEmailRequest(
    string? userId,
    string? token)
{
    /// <summary>
    /// Gets user id.
    /// </summary>
    public string? UserId { get; } = userId;
    /// <summary>
    /// Gets token.
    /// </summary>

    public string? Token { get; } = token;
}
