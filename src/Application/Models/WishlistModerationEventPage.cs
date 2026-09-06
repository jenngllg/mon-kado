using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains one page of administrator-only moderation history.</summary>
[ExcludeFromCodeCoverage]
public class WishlistModerationEventPage
{
    /// <summary>Gets the newest-first decisions in the requested page.</summary>
    public IEnumerable<WishlistModerationEventDetails> Items { get; init; } = [];
    /// <summary>Gets the one-based page number.</summary>
    public int CurrentPage
    {
        get; init;
    }
    /// <summary>Gets the page size.</summary>
    public int PageSize
    {
        get; init;
    }
    /// <summary>Gets the total number of decisions.</summary>
    public int TotalCount
    {
        get; init;
    }
}
