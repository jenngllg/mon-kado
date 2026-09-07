using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains read-only administrative report data.</summary>
[ExcludeFromCodeCoverage]
public class ReportedWishlistPage
{
    /// <summary>The page items.</summary>
    public IReadOnlyCollection<ReportedWishlistSummary> Items { get; init; } = [];
    /// <summary>The requested one-based page.</summary>
    public int CurrentPage
    {
        get; init;
    }
    /// <summary>The requested page size.</summary>
    public int PageSize
    {
        get; init;
    }
    /// <summary>The total matching item count.</summary>
    public int TotalCount
    {
        get; init;
    }
}
