using Microsoft.AspNetCore.Identity;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Rejects passwords that exceed the storage safety limit.
/// </summary>
/// <typeparam name="TUser">The Identity user type.</typeparam>
public class MaximumPasswordLengthValidator<TUser> : IPasswordValidator<TUser>
    where TUser : class
{
    private const int MaximumPasswordLength = 128;
    /// <summary>
    /// Executes the validate async operation.
    /// </summary>
    /// <param name="manager">The manager.</param>
    /// <param name="user">The user.</param>
    /// <param name="password">The password.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task<IdentityResult> ValidateAsync(
        UserManager<TUser> manager,
        TUser user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        return IsTooLong(password)
            ? Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"Passwords must not exceed {MaximumPasswordLength} characters."
            }))
            : Task.FromResult(IdentityResult.Success);
    }

    private static bool IsTooLong(string? password)
    {

        return password is not null && password.EnumerateRunes().Count() > MaximumPasswordLength;
    }
}
