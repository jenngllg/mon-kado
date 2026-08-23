using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class FailingMemberEmailChangeUserManager(
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
    public override Task<IdentityResult> ChangeEmailAsync(
        MonKadoUser user,
        string newEmail,
        string token)
    {

        return newEmail switch
        {
            "duplicate-email@example.fr" => Task.FromResult(IdentityResult.Failed(
                new IdentityError
                {
                    Code = "DuplicateEmail",
                    Description = "Duplicate email."
                })),
            "duplicate-user-name@example.fr" => Task.FromResult(IdentityResult.Failed(
                new IdentityError
                {
                    Code = "DuplicateUserName",
                    Description = "Duplicate user name."
                })),
            "unique-violation@example.fr" => Task.FromException<IdentityResult>(
                new DbUpdateException(
                    "Unique constraint violation.",
                    new PostgresException(
                        "Unique constraint violation.",
                        "ERROR",
                        "ERROR",
                        PostgresErrorCodes.UniqueViolation))),
            "concurrency-failure@example.fr" => Task.FromResult(IdentityResult.Failed(
                new IdentityError
                {
                    Code = "ConcurrencyFailure",
                    Description = "Concurrency failure."
                })),
            "concurrency-exception@example.fr" => Task.FromException<IdentityResult>(
                new DbUpdateConcurrencyException()),
            "generic-failure@example.fr" => Task.FromResult(IdentityResult.Failed(
                new IdentityError
                {
                    Code = "PersistenceFailed",
                    Description = "Persistence failed."
                })),
            _ => base.ChangeEmailAsync(
                user,
                newEmail,
                token)
        };
    }
}
