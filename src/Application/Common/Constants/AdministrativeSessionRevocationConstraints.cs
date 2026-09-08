using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Defines the retention and input limits of administrative session revocation.</summary>
[ExcludeFromCodeCoverage]
public static class AdministrativeSessionRevocationConstraints
{
    /// <summary>Gets the maximum normalized support reference length.</summary>
    public const int MaximumReferenceLength = 128;
    /// <summary>Gets the retained audit lifetime in calendar months.</summary>
    public const int AuditRetentionMonths = 6;
}
