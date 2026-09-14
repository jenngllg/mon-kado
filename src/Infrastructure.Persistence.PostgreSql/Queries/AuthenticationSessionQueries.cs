using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Queries;

/// <summary>Provides translatable session filters shared by Bearer and refresh validation.</summary>
public static class AuthenticationSessionQueries
{
    /// <summary>Requires current-factor proof for administrators and every member who already enabled MFA.</summary>
    /// <param name="context">The database context used by the outer authentication query.</param>
    /// <returns>A no-tracking query that never upgrades an old single-factor session after role promotion.</returns>
    public static IQueryable<AuthenticationSession> WithValidTwoFactor(MonKadoDbContext context)
    {

        return context.AuthenticationSessions
            .AsNoTracking()
            .Where(session => context.Users.Any(member => member.Id == session.UserId &&
                ((!member.TwoFactorEnabled &&
                    !context.MemberTwoFactors.Any(factor => factor.MemberId == member.Id && factor.CredentialId != null) &&
                    !context.UserRoles.Any(assignment => assignment.UserId == member.Id &&
                        context.Roles.Any(role => role.Id == assignment.RoleId && role.Name == RoleNames.Admin))) ||
                (session.TwoFactorCredentialId != null && session.TwoFactorVerifiedAt != null &&
                    context.MemberTwoFactors.Any(factor => factor.MemberId == member.Id &&
                        factor.CredentialId == session.TwoFactorCredentialId && factor.ProtectedSecret != null)))));
    }
}
