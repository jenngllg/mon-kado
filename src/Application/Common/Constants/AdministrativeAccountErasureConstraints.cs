using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Defines the approved erasure reference and retention boundaries.</summary>
[ExcludeFromCodeCoverage]
public static class AdministrativeAccountErasureConstraints
{
    /// <summary>Gets the maximum normalized reference length.</summary>
    public const int MaximumReferenceLength = 128;
    /// <summary>Gets the audit retention in calendar months.</summary>
    public const int AuditRetentionMonths = 6;
    /// <summary>Gets the maximum notification recipient lifetime in hours.</summary>
    public const int NotificationLifetimeHours = 24;
}
