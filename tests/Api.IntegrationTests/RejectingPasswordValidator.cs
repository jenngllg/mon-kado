using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class RejectingPasswordValidator(string errorCode) : IPasswordValidator<MonKadoUser>
{
    public Task<IdentityResult> ValidateAsync(
        UserManager<MonKadoUser> manager,
        MonKadoUser user,
        string? password)
    {
        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = errorCode,
            Description = errorCode
        }));
    }
}
