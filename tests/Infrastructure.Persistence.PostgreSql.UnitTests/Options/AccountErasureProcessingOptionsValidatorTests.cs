using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Options;

public class AccountErasureProcessingOptionsValidatorTests
{
    private readonly AccountErasureProcessingOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenDefaultsAreUsed_AcceptsBoundedPolicy()
    {
        // Arrange
        var options = new AccountErasureProcessingOptions();

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            options.PollInterval);
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            options.FailureInterval);
    }

    [Theory]
    [InlineData("batch-low")]
    [InlineData("batch-high")]
    [InlineData("attempts-low")]
    [InlineData("attempts-high")]
    [InlineData("lease-low")]
    [InlineData("lease-high")]
    [InlineData("poll")]
    [InlineData("failure")]
    [InlineData("maximum-low")]
    [InlineData("maximum-high")]
    [InlineData("empty")]
    [InlineData("delay-low")]
    [InlineData("delay-high")]
    [InlineData("unordered")]
    public void Validate_WhenBoundsAreInvalid_RejectsConfiguration(string scenario)
    {
        // Arrange
        var options = scenario switch
        {
            "batch-low" => new AccountErasureProcessingOptions { BatchSize = 0 },
            "batch-high" => new AccountErasureProcessingOptions { BatchSize = 1001 },
            "attempts-low" => new AccountErasureProcessingOptions { MaximumAttempts = 0 },
            "attempts-high" => new AccountErasureProcessingOptions { MaximumAttempts = 11 },
            "lease-low" => new AccountErasureProcessingOptions { LeaseDuration = TimeSpan.Zero },
            "lease-high" => new AccountErasureProcessingOptions { LeaseDuration = TimeSpan.FromHours(2) },
            "poll" => new AccountErasureProcessingOptions { PollInterval = TimeSpan.Zero },
            "failure" => new AccountErasureProcessingOptions { FailureInterval = TimeSpan.Zero },
            "maximum-low" => new AccountErasureProcessingOptions { MaximumRetryDelay = TimeSpan.Zero },
            "maximum-high" => new AccountErasureProcessingOptions { MaximumRetryDelay = TimeSpan.FromDays(2) },
            "empty" => new AccountErasureProcessingOptions { RetryDelays = [] },
            "delay-low" => new AccountErasureProcessingOptions { RetryDelays = [TimeSpan.Zero] },
            "delay-high" => new AccountErasureProcessingOptions { RetryDelays = [TimeSpan.FromDays(2)] },
            _ => new AccountErasureProcessingOptions
            {
                RetryDelays =
                [
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(1)
                ]
            }
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
    }
}
