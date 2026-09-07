using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Describes a member-owned asynchronous personal-data export.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportResponse
{
    /// <summary>Gets the export identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets queued, processing, ready, failed or expired.</summary>
    public PersonalDataExportStatus Status
    {
        get; init;
    }
    /// <summary>Gets the UTC request date.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>Gets the successful UTC snapshot date, or null before publication.</summary>
    public DateTime? SnapshotAt
    {
        get; init;
    }
    /// <summary>Gets the UTC publication date.</summary>
    public DateTime? ReadyAt
    {
        get; init;
    }
    /// <summary>Gets the fixed UTC archive expiration.</summary>
    public DateTime? ExpiresAt
    {
        get; init;
    }
    /// <summary>Gets the complete ZIP length.</summary>
    public long? SizeInBytes
    {
        get; init;
    }
    /// <summary>Gets a bounded terminal failure code, or null.</summary>
    public string? ErrorCode
    {
        get; init;
    }
}
