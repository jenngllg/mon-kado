using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Configuration;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Options;

public class WishlistModerationEmailOptionsValidatorTests
{
    private readonly WishlistModerationEmailOptionsValidator _validator = new();
    [Fact]
    public void Validate_WhenDefaultsAreUsed_AcceptsConfiguration()
    {
        // Arrange
        var options = new WishlistModerationEmailOptions();

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("BatchSize", "0")]
    [InlineData("BatchSize", "1001")]
    [InlineData("MaximumAttempts", "0")]
    [InlineData("MaximumAttempts", "11")]
    [InlineData("ProcessedRetentionDays", "0")]
    [InlineData("ProcessedRetentionDays", "366")]
    [InlineData("LeaseDuration", "00:00:00")]
    [InlineData("LeaseDuration", "01:00:01")]
    [InlineData("PollInterval", "00:00:00")]
    [InlineData("PollInterval", "01:00:01")]
    [InlineData("FailureInterval", "00:00:00")]
    [InlineData("FailureInterval", "01:00:01")]
    [InlineData("MaximumRetryDelay", "00:00:00")]
    [InlineData("MaximumRetryDelay", "8.00:00:00")]
    public void Validate_WhenBoundIsViolated_RejectsConfiguration(
        string property,
        string value)
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [property] = value })
            .Build();
        var options = new WishlistModerationEmailOptions();
        configuration.Bind(options);

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.NotEmpty(result.Failures);
    }

    [Theory]
    [InlineData("BatchSize", "1")]
    [InlineData("BatchSize", "1000")]
    [InlineData("MaximumAttempts", "1")]
    [InlineData("MaximumAttempts", "10")]
    [InlineData("ProcessedRetentionDays", "1")]
    [InlineData("ProcessedRetentionDays", "365")]
    [InlineData("LeaseDuration", "00:00:01")]
    [InlineData("LeaseDuration", "01:00:00")]
    [InlineData("MaximumRetryDelay", "7.00:00:00")]
    public void Validate_WhenValueIsAtValidBoundary_AcceptsConfiguration(
        string property,
        string value)
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [property] = value })
            .Build();
        var options = new WishlistModerationEmailOptions();
        configuration.Bind(options);

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { -1 })]
    [InlineData(new[] { 86401 })]
    [InlineData(new[] {
        60,
        30
    })]
    public void Validate_WhenRetrySequenceIsInvalid_RejectsConfiguration(int[] seconds)
    {
        // Arrange
        var options = new WishlistModerationEmailOptions
        {
            RetryDelays = seconds
                .Select(value => TimeSpan.FromSeconds(value))
                .ToArray()
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Validate_WhenConfigurationHasNoRetryArray_PreservesDefaults()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RetryDelays"] = null })
            .Build();
        var options = new WishlistModerationEmailOptions();
        configuration.Bind(options);

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.NotEmpty(options.RetryDelays);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenSeveralValuesAreInvalid_ReturnsEveryFailure()
    {
        // Arrange
        var options = new WishlistModerationEmailOptions
        {
            BatchSize = 0,
            MaximumAttempts = 11,
            ProcessedRetentionDays = 0,
            LeaseDuration = TimeSpan.Zero,
            MaximumRetryDelay = TimeSpan.Zero,
            RetryDelays = []
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
        Assert.Equal(
            6,
            result.Failures.Count());
    }
}
