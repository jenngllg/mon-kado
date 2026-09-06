using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains one page of public member search results.</summary>
[ExcludeFromCodeCoverage]
public class UserSearchPage
{
    /// <summary>Gets the matching members.</summary>
    public IReadOnlyCollection<UserSearchResult> Items { get; init; } = [];
    /// <summary>Gets the requested one-based page number.</summary>
    public int CurrentPage
    {
        get; init;
    }
    /// <summary>Gets the requested page size.</summary>
    public int PageSize
    {
        get; init;
    }
    /// <summary>Gets the total matching member count.</summary>
    public int TotalCount
    {
        get; init;
    }
}
