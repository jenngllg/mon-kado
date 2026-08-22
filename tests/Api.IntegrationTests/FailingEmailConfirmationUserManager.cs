using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class FailingEmailConfirmationUserManager(
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
    public override Task<bool> VerifyUserTokenAsync(
        MonKadoUser user,
        string tokenProvider,
        string purpose,
        string token)
    {
        return Task.FromResult(true);
    }

    public override Task<IdentityResult> ConfirmEmailAsync(
        MonKadoUser user,
        string token)
    {
        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "ConfirmationFailed",
            Description = "Confirmation failed."
        }));
    }
}
