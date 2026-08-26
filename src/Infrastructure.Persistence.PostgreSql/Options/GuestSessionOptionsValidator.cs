using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>
/// Validates guest-session configuration.
/// </summary>
public class GuestSessionOptionsValidator : IValidateOptions<GuestSessionOptions>
{
    private static readonly TimeSpan _maximumLifetime = TimeSpan.FromDays(180);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(
        string? name,
        GuestSessionOptions options)
    {
        _ = name;

        if (options.Lifetime <= TimeSpan.Zero || options.Lifetime > _maximumLifetime)
        {
            return ValidateOptionsResult.Fail(
                "'GuestSessions:Lifetime' must be greater than zero and at most 180 days.");
        }

        return ValidateOptionsResult.Success;
    }
}
