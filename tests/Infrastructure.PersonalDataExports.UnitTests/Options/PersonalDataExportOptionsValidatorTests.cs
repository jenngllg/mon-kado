using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.UnitTests.Options;

public class PersonalDataExportOptionsValidatorTests
{
    private readonly PersonalDataExportOptionsValidator _validator = new();
    [Fact]
    public void Validate_WhenDefaultsAreUsed_AcceptsBoundedConfiguration()
    {
        // Arrange
        var options = new PersonalDataExportOptions();

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void Validate_WhenLimitIsInvalid_ReportsItsConfigurationKey(
        PersonalDataExportOptions options,
        string key)
    {
        // Arrange

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                key,
                StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> InvalidConfigurations()
    {
        yield return [
            new PersonalDataExportOptions
            {
                ArchiveLifetime = TimeSpan.Zero
            },
            "ArchiveLifetime"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                ArchiveLifetime = TimeSpan.FromDays(2)
            },
            "ArchiveLifetime"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                RequestWindow = TimeSpan.Zero
            },
            "RequestWindow"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                RequestWindow = TimeSpan.FromDays(2)
            },
            "RequestWindow"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                MaximumRequests = 0
            },
            "MaximumRequests"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                MaximumRequests = 11
            },
            "MaximumRequests"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                MaximumAttempts = 0
            },
            "MaximumAttempts"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                MaximumAttempts = 11
            },
            "MaximumAttempts"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                MaximumArchiveBytes = 0
            },
            "MaximumArchiveBytes"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                MaximumArchiveBytes = long.MaxValue
            },
            "MaximumArchiveBytes"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                AttemptTimeout = TimeSpan.Zero
            },
            "AttemptTimeout"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                AttemptTimeout = TimeSpan.FromHours(2)
            },
            "AttemptTimeout"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                LeaseDuration = TimeSpan.Zero
            },
            "LeaseDuration"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                LeaseDuration = TimeSpan.FromMinutes(11)
            },
            "LeaseDuration"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                LeaseRenewalInterval = TimeSpan.Zero
            },
            "LeaseRenewalInterval"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                LeaseRenewalInterval = TimeSpan.FromMinutes(2)
            },
            "LeaseRenewalInterval"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                PollInterval = TimeSpan.Zero
            },
            "intervals"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                FailureInterval = TimeSpan.Zero
            },
            "intervals"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                PollInterval = TimeSpan.FromHours(2)
            },
            "intervals"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                FailureInterval = TimeSpan.FromHours(2)
            },
            "intervals"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                CleanupBatchSize = 0
            },
            "CleanupBatchSize"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                CleanupBatchSize = 1001
            },
            "CleanupBatchSize"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                TemporaryGracePeriod = TimeSpan.Zero
            },
            "TemporaryGracePeriod"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                AttemptTimeout = TimeSpan.FromMinutes(1),
                TemporaryGracePeriod = TimeSpan.FromMinutes(2)
            },
            "TemporaryGracePeriod"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                RetryDelays = []
            },
            "RetryDelays"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                RetryDelays = [TimeSpan.Zero]
            },
            "RetryDelays"
        ];
        yield return [
            new PersonalDataExportOptions
            {
                RetryDelays = [TimeSpan.FromHours(2)]
            },
            "RetryDelays"
        ];
    }
}
