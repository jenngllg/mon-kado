using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents a bearer access token response.
/// </summary>
/// <param name="accessToken">The encoded access token.</param>
/// <param name="tokenType">The authorization scheme.</param>
/// <param name="expiresIn">The token lifetime in seconds.</param>
[ExcludeFromCodeCoverage]
public class AccessTokenResponse(
    string accessToken,
    string tokenType,
    int expiresIn)
{
    /// <summary>
    /// Gets the encoded access token.
    /// </summary>
    public string AccessToken { get; } = accessToken;

    /// <summary>
    /// Gets the authorization scheme.
    /// </summary>
    public string TokenType { get; } = tokenType;

    /// <summary>
    /// Gets the token lifetime in seconds.
    /// </summary>
    public int ExpiresIn { get; } = expiresIn;
}
