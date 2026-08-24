using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL operations for Google external logins.
/// </summary>
/// <param name="context">The database context.</param>
public class GoogleAccountRepository(MonKadoDbContext context) : IGoogleAccountRepository
{
    /// <inheritdoc />
    public void AddLogin(
        Guid memberId,
        string subject)
    {
        context.UserLogins.Add(new IdentityUserLogin<Guid>
        {
            LoginProvider = ExternalLoginProviders.Google,
            ProviderDisplayName = ExternalLoginProviders.Google,
            ProviderKey = subject,
            UserId = memberId
        });
    }

    /// <inheritdoc />
    public Task<Guid?> GetMemberIdBySubjectAsync(
        string subject,
        CancellationToken cancellationToken)
    {

        return context.UserLogins
            .AsNoTracking()
            .Where(login =>
                login.LoginProvider == ExternalLoginProviders.Google &&
                login.ProviderKey == subject)
            .Select(login => (Guid?)login.UserId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetSubjectByMemberIdAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {

        return context.UserLogins
            .AsNoTracking()
            .Where(login =>
                login.UserId == memberId &&
                login.LoginProvider == ExternalLoginProviders.Google)
            .Select(login => login.ProviderKey)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
