using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Defines profile-photo normalization limits.</summary>
[ExcludeFromCodeCoverage]
public static class ProfileImageConstraints
{
    /// <summary>The maximum normalized profile-photo edge length in pixels.</summary>
    public const int MaximumOutputEdgeLength = 512;
}
