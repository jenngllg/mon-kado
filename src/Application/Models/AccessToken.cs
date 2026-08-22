using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents a signed access token.
/// </summary>
/// <param name="value">The encoded token.</param>
/// <param name="expiresIn">The lifetime in seconds.</param>
[ExcludeFromCodeCoverage]
public class AccessToken(
    string value,
    int expiresIn)
{
    /// <summary>
    /// Gets the encoded token.
    /// </summary>
    public string Value { get; } = value;

    /// <summary>
    /// Gets the lifetime in seconds.
    /// </summary>
    public int ExpiresIn { get; } = expiresIn;
}
