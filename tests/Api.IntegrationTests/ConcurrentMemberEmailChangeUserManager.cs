using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class ConcurrentMemberEmailChangeUserManager(
    IUserStore<MonKadoUser> store,
    IOptions<IdentityOptions> options,
    IPasswordHasher<MonKadoUser> passwordHasher,
    IEnumerable<IUserValidator<MonKadoUser>> userValidators,
    IEnumerable<IPasswordValidator<MonKadoUser>> passwordValidators,
    ILookupNormalizer lookupNormalizer,
    IdentityErrorDescriber errorDescriber,
    IServiceProvider serviceProvider,
    ILogger<UserManager<MonKadoUser>> logger,
    ConcurrentEmailChangeCoordinator coordinator)
    : UserManager<MonKadoUser>(
        store,
        options,
        passwordHasher,
        userValidators,
        passwordValidators,
        lookupNormalizer,
        errorDescriber,
        serviceProvider,
        logger)
{
    public override async Task<IdentityResult> ChangeEmailAsync(
        MonKadoUser user,
        string newEmail,
        string token)
    {
        await coordinator.WaitAsync();

        return await base.ChangeEmailAsync(
            user,
            newEmail,
            token);
    }
}
