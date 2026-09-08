using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Identifies the external request requiring an administrative data export.</summary>
[ExcludeFromCodeCoverage]
public class RequestAdministrativeDataExportRequest
{
    /// <summary>Gets the mandatory technical request reference, without personal information.</summary>
    public string? RequestReference
    {
        get; init;
    }
}
