using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Worker.Options;
/// <summary>
/// Represents gmail options.
/// </summary>

[ExcludeFromCodeCoverage]
public class GmailOptions
{
    /// <summary>
    /// Identifies section name.
    /// </summary>
    public const string SectionName = "Gmail";
    /// <summary>
    /// Gets sender address.
    /// </summary>

    public string SenderAddress
    {
        get; init;
    } = string.Empty;
    /// <summary>
    /// Gets client id.
    /// </summary>

    public string? ClientId
    {
        get; init;
    }
    /// <summary>
    /// Gets client secret.
    /// </summary>

    public string? ClientSecret
    {
        get; init;
    }
    /// <summary>
    /// Gets refresh token.
    /// </summary>

    public string? RefreshToken
    {
        get; init;
    }
}
