using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>
/// Validates JWT authentication options.
/// </summary>
public class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private const int MinimumSigningKeyLength = 32;

    /// <summary>
    /// Validates JWT authentication options.
    /// </summary>
    /// <param name="name">The options name.</param>
    /// <param name="options">The options.</param>
    /// <returns>The validation result.</returns>
    public ValidateOptionsResult Validate(
        string? name,
        JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
            failures.Add("Jwt:Issuer is required.");

        if (string.IsNullOrWhiteSpace(options.Audience))
            failures.Add("Jwt:Audience is required.");

        if (!HasValidSigningKey(options.SigningKey))
            failures.Add("Jwt:SigningKey must be Base64 encoded and contain at least 256 bits.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    internal static bool HasValidSigningKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            return Convert.FromBase64String(value).Length >= MinimumSigningKeyLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
