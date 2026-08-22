using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents refresh token material.
/// </summary>
/// <param name="value">The value returned to the browser.</param>
/// <param name="hash">The hash stored by the server.</param>
[ExcludeFromCodeCoverage]
public class RefreshToken(
    string value,
    byte[] hash)
{
    /// <summary>
    /// Gets the value returned to the browser.
    /// </summary>
    public string Value { get; } = value;

    /// <summary>
    /// Gets the hash stored by the server.
    /// </summary>
    public byte[] Hash { get; } = hash;
}
