using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests a page of confirmed members matching a display-name substring.</summary>
/// <param name="displayName">The required display-name search term.</param>
/// <param name="page">The optional one-based page number.</param>
/// <param name="pageSize">The optional page size.</param>
public class SearchUsersQuery(
    string? displayName,
    int? page,
    int? pageSize) : IRequest<UserSearchPage>
{
    /// <summary>Gets the default page number.</summary>
    public const int DefaultPage = 1;
    /// <summary>Gets the default page size.</summary>
    public const int DefaultPageSize = 20;
    /// <summary>Gets the maximum page size.</summary>
    public const int MaximumPageSize = 100;
    /// <summary>Gets the nullable incoming search term.</summary>
    public string? DisplayName { get; } = displayName;
    /// <summary>Gets the nullable incoming page number.</summary>
    public int? Page { get; } = page;
    /// <summary>Gets the nullable incoming page size.</summary>
    public int? PageSize { get; } = pageSize;
}

/// <summary>Coordinates public member searches without logging search terms or results.</summary>
/// <param name="service">The member search service.</param>
/// <param name="logger">The structured logger.</param>
public class SearchUsersQueryHandler(
    IUserSearchService service,
    ILogger<SearchUsersQueryHandler> logger) : IRequestHandler<SearchUsersQuery, UserSearchPage>
{
    /// <summary>Normalizes a validated search term and retrieves the requested page.</summary>
    /// <param name="request">The validated search query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public search result page.</returns>
    public async Task<UserSearchPage> Handle(
        SearchUsersQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.UserSearchStarted(logger);
        var displayName = (request.DisplayName ?? string.Empty)
            .Trim()
            .Normalize(NormalizationForm.FormC);
        var result = await service.SearchAsync(
            displayName,
            request.Page ?? SearchUsersQuery.DefaultPage,
            request.PageSize ?? SearchUsersQuery.DefaultPageSize,
            cancellationToken);
        ApplicationLogMessages.UserSearchCompleted(logger);

        return result;
    }
}
