using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>Returns a controlled public identity without accessing PostgreSQL.</summary>
public class RecordingUserSearchService : IUserSearchService
{
    /// <summary>Gets the last normalized search term.</summary>
    public string? LastTerm
    {
        get; private set;
    }

    /// <inheritdoc/>
    public Task<UserSearchPage> SearchAsync(
        string displayName,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        LastTerm = displayName;

        return Task.FromResult(new UserSearchPage
        {
            Items = [new UserSearchResult {
                        Id = Guid.CreateVersion7(),
                        DisplayName = "ConfidentialResultName"
                    }],
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = 1
        });
    }
}
