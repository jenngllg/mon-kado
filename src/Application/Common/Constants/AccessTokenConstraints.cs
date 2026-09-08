using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Defines the shared access-token validation and retention tolerances.</summary>
[ExcludeFromCodeCoverage]
public static class AccessTokenConstraints
{
    /// <summary>Gets the allowed clock skew in seconds.</summary>
    public const int ClockSkewSeconds = 30;
}
