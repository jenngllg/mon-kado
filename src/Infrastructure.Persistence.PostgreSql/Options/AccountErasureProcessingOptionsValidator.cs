using JennGllg.Fr.MonKado.Back.Application.Options;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

/// <summary>Validates bounded erasure processing settings before either host starts.</summary>
public class AccountErasureProcessingOptionsValidator : IValidateOptions<AccountErasureProcessingOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(
        string? name,
        AccountErasureProcessingOptions options)
    {
        var failures = new List<string>();

        if (options.BatchSize is < 1 or > 1000)
            failures.Add("AccountErasureProcessing:BatchSize must be between 1 and 1000.");

        if (options.MaximumAttempts is < 1 or > 10)
            failures.Add("AccountErasureProcessing:MaximumAttempts must be between 1 and 10.");
        var durations = new[]
        {
            options.LeaseDuration,
            options.PollInterval,
            options.FailureInterval
        };

        if (durations.Any(value => value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromHours(1)))
            failures.Add("Erasure lease and cycle intervals must be between one second and one hour.");

        if (options.MaximumRetryDelay < TimeSpan.FromSeconds(1) || options.MaximumRetryDelay > TimeSpan.FromHours(24))
            failures.Add("Erasure maximum retry delay must be between one second and 24 hours.");

        if (options.RetryDelays.Length == 0 || options.RetryDelays.Any(value => value < TimeSpan.FromSeconds(1) || value > options.MaximumRetryDelay) || !options.RetryDelays.SequenceEqual(options.RetryDelays.Order()))
            failures.Add("Erasure retry delays must be nonempty, positive, bounded and ordered.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
