using System.Text;
using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class MaximumPasswordLengthValidator<TUser> : IPasswordValidator<TUser>
    where TUser : class
{
    private const int MaximumPasswordLength = 128;

    public Task<IdentityResult> ValidateAsync(
        UserManager<TUser> manager,
        TUser user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (password is not null && password.EnumerateRunes().Count() > MaximumPasswordLength)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"Passwords must not exceed {MaximumPasswordLength} characters."
            }));
        }

        return Task.FromResult(IdentityResult.Success);
    }
}
