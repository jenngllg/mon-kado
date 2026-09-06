using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>Rejects invalid account deletion lifetime and request limits at startup.</summary>
public class MemberAccountDeletionOptionsValidator : IValidateOptions<MemberAccountDeletionOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(
        string? name,
        MemberAccountDeletionOptions options)
    {

        if (options.Lifetime <= TimeSpan.Zero || options.Lifetime > TimeSpan.FromHours(1))
            return ValidateOptionsResult.Fail("MemberAccountDeletion:Lifetime must be positive and at most one hour.");

        if (options.RequestWindow <= TimeSpan.Zero || options.RequestWindow > TimeSpan.FromDays(1))
            return ValidateOptionsResult.Fail("MemberAccountDeletion:RequestWindow must be positive and at most one day.");

        if (options.MaximumRequests < 1 || options.MaximumRequests > 10)
            return ValidateOptionsResult.Fail("MemberAccountDeletion:MaximumRequests must be between 1 and 10.");

        return ValidateOptionsResult.Success;
    }
}
