using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Executes bounded, literal public member searches entirely in PostgreSQL.</summary>
/// <param name="context">The scoped database context.</param>
public class UserSearchService(MonKadoDbContext context) : IUserSearchService
{
    /// <inheritdoc/>
    public async Task<UserSearchPage> SearchAsync(
        string displayName,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var totalCount = await context.Database
                .SqlQuery<int>($"""
                SELECT count(*)::integer AS "Value"
                    FROM public.users
                WHERE email_confirmed AND length(public.unaccent({displayName})) > 0
                AND strpos(lower(public.unaccent(normalize(display_name, NFC))), lower(public.unaccent({displayName}))) > 0
            """)
                .SingleAsync(cancellationToken);
            var offset = (long)(page - 1) * pageSize;
            var items = await context.Database
                .SqlQuery<UserSearchResult>($"""
                SELECT id, display_name
                    FROM public.users
                WHERE email_confirmed AND length(public.unaccent({displayName})) > 0
                AND strpos(lower(public.unaccent(normalize(display_name, NFC))), lower(public.unaccent({displayName}))) > 0
                    ORDER BY lower(public.unaccent(normalize(display_name, NFC))), id
                LIMIT {pageSize} OFFSET {offset}
            """)
                .ToArrayAsync(cancellationToken);

            return new UserSearchPage
            {
                Items = items,
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
