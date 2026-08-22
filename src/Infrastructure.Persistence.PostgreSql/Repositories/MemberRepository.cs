using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for member identity and roles.
/// </summary>
/// <param name="context">The database context.</param>
public class MemberRepository(MonKadoDbContext context) : IMemberRepository
{
    /// <inheritdoc />
    public void AddMemberRole(Guid memberId)
    {
        context.UserRoles.Add(new IdentityUserRole<Guid>
        {
            RoleId = RoleIds.Member,
            UserId = memberId
        });
    }

    /// <inheritdoc />
    public Task<CurrentSession?> GetCurrentSessionAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {

        return context.Users
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(member => new CurrentSession(
                member.Id,
                member.Email!,
                member.DisplayName,
                context.UserRoles
                    .AsNoTracking()
                    .Where(memberRole => memberRole.UserId == member.Id)
                    .Join(
                        context.Roles.AsNoTracking(),
                        memberRole => memberRole.RoleId,
                        role => role.Id,
                        (
                            _,
                            role) => role.Name!)
                    .OrderBy(role => role)
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
