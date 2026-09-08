using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Defines the agreed administrative reference and audit-retention limits.</summary>
[ExcludeFromCodeCoverage]
public static class AdministrativeDataExportConstraints
{
    /// <summary>Gets the maximum length of a request reference.</summary>
    public const int MaximumReferenceLength = 128;
    /// <summary>Gets the number of calendar months for which audit events are retained.</summary>
    public const int AuditRetentionMonths = 6;
}
