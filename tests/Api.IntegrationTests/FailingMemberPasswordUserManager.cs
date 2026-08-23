using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class FailingMemberPasswordUserManager(
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
    public override Task<IdentityResult> ChangePasswordAsync(
        MonKadoUser user,
        string currentPassword,
        string newPassword)
    {

        return newPassword switch
        {
            "password mismatch failure" => Task.FromResult(CreateFailure(
                "PasswordMismatch",
                "Password mismatch.")),
            "password short failure" => Task.FromResult(CreateFailure(
                "PasswordTooShort",
                "Password too short.")),
            "password long failure" => Task.FromResult(CreateFailure(
                "PasswordTooLong",
                "Password too long.")),
            "password digit failure" => Task.FromResult(CreateFailure(
                "PasswordRequiresDigit",
                "The password requires a digit.")),
            "password unique failure" => Task.FromResult(CreateFailure(
                "PasswordRequiresUniqueChars",
                "The password requires more unique characters.")),
            "password symbol failure" => Task.FromResult(CreateFailure(
                "PasswordRequiresNonAlphanumeric",
                "The password requires a non-alphanumeric character.")),
            "password lower failure" => Task.FromResult(CreateFailure(
                "PasswordRequiresLower",
                "The password requires a lowercase letter.")),
            "password upper failure" => Task.FromResult(CreateFailure(
                "PasswordRequiresUpper",
                "The password requires an uppercase letter.")),
            "password unexpected failure" => Task.FromResult(CreateFailure(
                "PersistenceFailed",
                "Persistence failed.")),
            "password timeout failure" => Task.FromException<IdentityResult>(
                new TimeoutException("PostgreSQL timed out.")),
            _ => base.ChangePasswordAsync(
                user,
                currentPassword,
                newPassword)
        };
    }

    private static IdentityResult CreateFailure(
        string code,
        string description)
    {

        return IdentityResult.Failed(new IdentityError
        {
            Code = code,
            Description = description
        });
    }
}
