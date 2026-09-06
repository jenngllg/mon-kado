using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Options;

/// <summary>Validates moderation delivery bounds before the Worker starts.</summary>
public class WishlistModerationEmailOptionsValidator : IValidateOptions<WishlistModerationEmailOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(
        string? name,
        WishlistModerationEmailOptions options)
    {
        var failures = new List<string>();

        if (options.BatchSize is < 1 or > 1000)
            failures.Add("WishlistModerationEmail:BatchSize must be between 1 and 1000.");

        if (options.MaximumAttempts is < 1 or > 10)
            failures.Add("WishlistModerationEmail:MaximumAttempts must be between 1 and 10.");

        if (options.ProcessedRetentionDays is < 1 or > 365)
            failures.Add("WishlistModerationEmail:ProcessedRetentionDays must be between 1 and 365.");
        var durations = new[]
        {
            options.LeaseDuration,
            options.PollInterval,
            options.FailureInterval
        };

        if (durations.Any(value => value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromHours(1)))
            failures.Add("Moderation lease and cycle intervals must be between one second and one hour.");

        if (options.MaximumRetryDelay < TimeSpan.FromSeconds(1) || options.MaximumRetryDelay > TimeSpan.FromDays(7))
            failures.Add("Moderation maximum retry delay must be between one second and seven days.");

        if (options.RetryDelays.Length == 0 || options.RetryDelays.Any(value => value < TimeSpan.FromSeconds(1) || value > options.MaximumRetryDelay) || !options.RetryDelays.SequenceEqual(options.RetryDelays.Order()))
            failures.Add("Moderation retry delays must be nonempty, positive, bounded and ordered.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
