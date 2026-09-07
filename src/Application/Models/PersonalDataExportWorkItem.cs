using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Identifies one fenced attempt owned by a worker.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportWorkItem
{
    /// <summary>Gets the export identifier.</summary>
    public Guid ExportId
    {
        get; init;
    }
    /// <summary>Gets the member whose data may be exported.</summary>
    public Guid MemberId
    {
        get; init;
    }
    /// <summary>Gets the unique lease and immutable archive-attempt identifier.</summary>
    public Guid LeaseId
    {
        get; init;
    }
    /// <summary>Gets the one-based attempt number.</summary>
    public int AttemptCount
    {
        get; init;
    }
}
