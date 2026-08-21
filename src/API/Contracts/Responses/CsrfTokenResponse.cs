using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
/// <summary>
/// Represents csrf token response.
/// </summary>
/// <param name="token">The token.</param>

[ExcludeFromCodeCoverage]
public class CsrfTokenResponse(string token)
{
    /// <summary>
    /// Gets token.
    /// </summary>
    public string Token { get; } = token;
}
