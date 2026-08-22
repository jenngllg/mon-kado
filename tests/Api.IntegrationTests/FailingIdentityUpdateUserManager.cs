using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class FailingIdentityUpdateUserManager(
    IUserStore<MonKadoUser> store,
    IOptions<IdentityOptions> options,
    IPasswordHasher<MonKadoUser> passwordHasher,
    IEnumerable<IUserValidator<MonKadoUser>> userValidators,
    IEnumerable<IPasswordValidator<MonKadoUser>> passwordValidators,
    ILookupNormalizer lookupNormalizer,
    IdentityErrorDescriber errorDescriber,
    IServiceProvider serviceProvider,
    ILogger<UserManager<MonKadoUser>> logger)
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
    public override Task<IdentityResult> AccessFailedAsync(MonKadoUser user)
    {
        return Task.FromResult(CreateFailure());
    }

    public override Task<IdentityResult> ResetAccessFailedCountAsync(MonKadoUser user)
    {
        return Task.FromResult(CreateFailure());
    }

    private static IdentityResult CreateFailure()
    {
        return IdentityResult.Failed(new IdentityError
        {
            Code = "PersistenceFailed",
            Description = "Persistence failed."
        });
    }
}
