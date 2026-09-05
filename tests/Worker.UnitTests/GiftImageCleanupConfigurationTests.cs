using JennGllg.Fr.MonKado.Back.Worker.Configurations;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class GiftImageCleanupConfigurationTests
{
    [Fact]
    public void ConfigureGiftImageCleanup_WhenConfigurationIsValid_BindsOptions()
    {
        // Arrange
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["GiftImageCleanup:BatchSize"] = "25",
            ["GiftImageCleanup:Interval"] = "00:01:00"
        });
        var services = new ServiceCollection();

        // Act
        services.ConfigureGiftImageCleanup(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GiftImageCleanupOptions>>().Value;

        // Assert
        Assert.Equal(
            25,
            options.BatchSize);
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            options.Interval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void ConfigureGiftImageCleanup_WhenBatchSizeIsInvalid_ThrowsInvalidOperation(
        int batchSize)
    {
        // Arrange
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["GiftImageCleanup:BatchSize"] = batchSize.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        });
        var services = new ServiceCollection();

        // Act
        var exception = Record.Exception(() => services.ConfigureGiftImageCleanup(configuration));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Theory]
    [InlineData("Interval", "00:00:00")]
    [InlineData("FailureRetryInterval", "-00:00:01")]
    [InlineData("LeaseDuration", "00:00:00")]
    [InlineData("PendingGracePeriod", "00:04:59")]
    [InlineData("MaximumRetryDelay", "00:00:59")]
    public void ConfigureGiftImageCleanup_WhenDurationIsInvalid_ThrowsInvalidOperation(
        string setting,
        string value)
    {
        // Arrange
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            [$"GiftImageCleanup:{setting}"] = value
        });
        var services = new ServiceCollection();

        // Act
        var exception = Record.Exception(() => services.ConfigureGiftImageCleanup(configuration));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    private static IConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
