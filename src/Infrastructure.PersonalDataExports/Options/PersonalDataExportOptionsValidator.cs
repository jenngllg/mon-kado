using JennGllg.Fr.MonKado.Back.Application.Options;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;

/// <summary>Validates bounded worker limits and renewal timing before startup.</summary>
public class PersonalDataExportOptionsValidator : IValidateOptions<PersonalDataExportOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(
        string? name,
        PersonalDataExportOptions options)
    {
        var failures = new List<string>();

        if (options.ArchiveLifetime <= TimeSpan.Zero || options.ArchiveLifetime > TimeSpan.FromHours(24))
            failures.Add("PersonalDataExports:ArchiveLifetime must be positive and at most 24 hours.");

        if (options.RequestWindow <= TimeSpan.Zero || options.RequestWindow > TimeSpan.FromDays(1))
            failures.Add("PersonalDataExports:RequestWindow must be positive and at most 24 hours.");

        if (options.MaximumRequests is < 1 or > 10)
            failures.Add("PersonalDataExports:MaximumRequests must be between 1 and 10.");

        if (options.MaximumAttempts is < 1 or > 10)
            failures.Add("PersonalDataExports:MaximumAttempts must be between 1 and 10.");

        if (options.MaximumArchiveBytes is < 1 or > 10L * 1024 * 1024 * 1024)
            failures.Add("PersonalDataExports:MaximumArchiveBytes must be positive and at most 10 GiB.");

        if (options.AttemptTimeout <= TimeSpan.Zero || options.AttemptTimeout > TimeSpan.FromHours(1))
            failures.Add("PersonalDataExports:AttemptTimeout must be positive and at most one hour.");

        if (options.LeaseDuration <= TimeSpan.Zero || options.LeaseDuration > TimeSpan.FromMinutes(10))
            failures.Add("PersonalDataExports:LeaseDuration must be positive and at most ten minutes.");

        if (options.LeaseRenewalInterval <= TimeSpan.Zero || options.LeaseRenewalInterval >= options.LeaseDuration)
            failures.Add("PersonalDataExports:LeaseRenewalInterval must be positive and shorter than the lease.");

        if (options.PollInterval <= TimeSpan.Zero || options.PollInterval > TimeSpan.FromHours(1) || options.FailureInterval <= TimeSpan.Zero || options.FailureInterval > TimeSpan.FromHours(1))
            failures.Add("PersonalDataExports worker intervals must be positive and at most one hour.");

        if (options.CleanupBatchSize is < 1 or > 1000)
            failures.Add("PersonalDataExports:CleanupBatchSize must be between 1 and 1000.");

        if (options.TemporaryGracePeriod <= options.AttemptTimeout || options.TemporaryGracePeriod <= options.LeaseDuration)
            failures.Add("PersonalDataExports:TemporaryGracePeriod must exceed the attempt timeout and lease duration.");

        if (options.RetryDelays.Length == 0 || options.RetryDelays.Any(delay => delay <= TimeSpan.Zero || delay > TimeSpan.FromHours(1)))
            failures.Add("PersonalDataExports:RetryDelays must contain positive delays of at most one hour.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
