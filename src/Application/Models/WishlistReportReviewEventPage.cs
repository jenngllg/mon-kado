using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains private administrative report review data.</summary>
[ExcludeFromCodeCoverage]
public class WishlistReportReviewEventPage
{
    /// <summary>The current page items.</summary>
    public IReadOnlyCollection<WishlistReportReviewEventDetails> Items { get; init; } = [];
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
    /// <summary>The total matching event count.</summary>
    public int TotalCount
    {
        get; init;
    }
}
