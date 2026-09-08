using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains read-only administrative audit data.</summary>
[ExcludeFromCodeCoverage]
public class AdministrativeAuditPage
{
    /// <summary>Gets the Items value.</summary>
    public IEnumerable<AdministrativeAuditEventDetails> Items { get; init; } = [];
    /// <summary>Gets the CurrentPage value.</summary>
    public int CurrentPage
    {
        get; init;
    }
    /// <summary>Gets the PageSize value.</summary>
    public int PageSize
    {
        get; init;
    }
    /// <summary>Gets the TotalCount value.</summary>
    public int TotalCount
    {
        get; init;
    }
}
