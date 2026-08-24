using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>
/// Defines the external login provider names persisted by MonKado.
/// </summary>
[ExcludeFromCodeCoverage]
public static class ExternalLoginProviders
{
    /// <summary>
    /// Identifies Google OpenID Connect accounts.
    /// </summary>
    public const string Google = "Google";
}
