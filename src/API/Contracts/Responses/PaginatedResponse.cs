namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>
/// Represents one page of API results.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="items">The current page items.</param>
/// <param name="currentPage">The requested one-based page number.</param>
/// <param name="pageSize">The requested page size.</param>
/// <param name="totalCount">The total matching item count.</param>
public class PaginatedResponse<T>(
    IEnumerable<T> items,
    int currentPage,
    int pageSize,
    int totalCount)
{
    /// <summary>Gets the current page items.</summary>
    public IEnumerable<T> Items { get; } = items;

    /// <summary>Gets the requested one-based page number.</summary>
    public int CurrentPage { get; } = currentPage;

    /// <summary>Gets the requested page size.</summary>
    public int PageSize { get; } = pageSize;

    /// <summary>Gets the total matching item count.</summary>
    public int TotalCount { get; } = totalCount;

    /// <summary>Gets the total number of pages containing matching items.</summary>
    public int TotalPages
    {
        get;
    } = totalCount == 0
        ? 0
        : (int)(((long)totalCount + pageSize - 1) / pageSize);

    /// <summary>Gets whether a preceding page containing items exists.</summary>
    public bool HasPreviousPage => TotalPages > 0 && CurrentPage > 1;

    /// <summary>Gets whether a following page containing items exists.</summary>
    public bool HasNextPage => CurrentPage < TotalPages;
}
