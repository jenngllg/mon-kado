using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Resolves administrator authorization from current PostgreSQL role assignments.</summary>
/// <param name="context">The scoped database context.</param>
public class AdministratorAccessService(MonKadoDbContext context) : IAdministratorAccessService
{
    /// <inheritdoc/>
    public async Task<AdministratorAccess> GetAccessAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var access = await context.Users
                .AsNoTracking()
                .Where(member => member.Id == memberId)
                .Select(member => context.UserRoles
                    .Join(
                    context.Roles,
                    assignment => assignment.RoleId,
                    role => role.Id,
                    (
                        assignment,
                        role) => new
                        {
                            assignment.UserId,
                            role.Name
                        })
                    .Any(role => role.UserId == member.Id && role.Name == RoleNames.Admin))
                .Cast<bool?>()
                .SingleOrDefaultAsync(cancellationToken);

            if (access is null)
                return AdministratorAccess.MemberNotFound;

            return access.Value ? AdministratorAccess.Granted : AdministratorAccess.Forbidden;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
