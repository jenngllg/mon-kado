using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;

/// <summary>Rejects unbounded snapshot intervals and nonlocal service identities.</summary>
public class ObservabilityOptionsValidator : IValidateOptions<ObservabilityOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(
        string? name,
        ObservabilityOptions options)
    {

        if (options.Service is not ("api" or "worker")
            || string.IsNullOrEmpty(options.Version)
            || options.Version != "local" && (options.Version.Length != 40 || !options.Version.All(char.IsAsciiHexDigit))
            || options.Interval < TimeSpan.FromSeconds(5)
            || options.Interval > TimeSpan.FromSeconds(60)
            || options.Enabled && (string.IsNullOrWhiteSpace(options.Directory)
                || !Path.IsPathFullyQualified(options.Directory)))
            return ValidateOptionsResult.Fail("Observability requires a known service, an absolute directory and a 5–60 second interval.");

        return ValidateOptionsResult.Success;
    }
}
