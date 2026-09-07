using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Describes a member-owned export without exposing storage or lease credentials.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportDetails
{
    /// <summary>Gets the export identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the public lifecycle state.</summary>
    public PersonalDataExportStatus Status
    {
        get; init;
    }
    /// <summary>Gets the UTC request date.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>Gets the UTC database snapshot date after successful preparation.</summary>
    public DateTime? SnapshotAt
    {
        get; init;
    }
    /// <summary>Gets the UTC publication date.</summary>
    public DateTime? ReadyAt
    {
        get; init;
    }
    /// <summary>Gets the absolute UTC download deadline.</summary>
    public DateTime? ExpiresAt
    {
        get; init;
    }
    /// <summary>Gets the complete archive size.</summary>
    public long? SizeInBytes
    {
        get; init;
    }
    /// <summary>Gets the bounded terminal failure classification.</summary>
    public PersonalDataExportFailure? Failure
    {
        get; init;
    }
}
