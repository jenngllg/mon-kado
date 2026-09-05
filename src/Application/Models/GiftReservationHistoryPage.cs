using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents one page of member reservation history.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftReservationHistoryPage
{
    /// <summary>Gets the history entries.</summary>
    public IReadOnlyCollection<GiftReservationHistoryDetails> Items
    {
        get; init;
    } = [];

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

    /// <summary>Gets the total matching entry count.</summary>
    public int TotalCount
    {
        get; init;
    }
}
