using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains a page of accessible followed lists.</summary>
[ExcludeFromCodeCoverage]
public class WishlistSubscriptionPage
{
    /// <summary>Gets the page summaries.</summary>
    public IReadOnlyCollection<WishlistSubscriptionDetails> Items { get; init; } = [];
    /// <summary>Gets the requested page number.</summary>
    public int CurrentPage
    {
        get; init;
    }
    /// <summary>Gets the page size.</summary>
    public int PageSize
    {
        get; init;
    }
    /// <summary>Gets the count of accessible subscriptions.</summary>
    public int TotalCount
    {
        get; init;
    }
}
