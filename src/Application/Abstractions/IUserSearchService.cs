using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Searches confirmed members without exposing private account data.</summary>
public interface IUserSearchService
{
    /// <summary>Searches a literal display-name substring without case or accent distinctions.</summary>
    /// <param name="displayName">The validated, trimmed search term.</param>
    /// <param name="page">The one-based page number.</param>
    /// <param name="pageSize">The bounded page size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The requested page of public identities.</returns>
    Task<UserSearchPage> SearchAsync(
        string displayName,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
