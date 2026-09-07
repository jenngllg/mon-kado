using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Models;

public class WishlistModerationEmailDeliveryPolicyTests
{
    private readonly WishlistModerationEmailDeliveryPolicy _policy = new(
        20,
        TimeSpan.FromMinutes(2),
        10,
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15)
        ],
        TimeSpan.FromHours(1),
        TimeSpan.FromDays(30));
    [Theory]
    [InlineData(0, null, 1)]
    [InlineData(1, null, 1)]
    [InlineData(2, null, 5)]
    [InlineData(3, null, 15)]
    [InlineData(10, null, 15)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 3, 5)]
    [InlineData(2, 5, 5)]
    [InlineData(2, 8, 8)]
    [InlineData(2, 60, 60)]
    [InlineData(2, 120, 60)]
    public void GetRetryDelay_WhenProviderGuidanceVaries_UsesBoundedConfiguredDelay(
        int attempt,
        int? requestedMinutes,
        int expectedMinutes)
    {
        // Arrange
        var retryAfter = requestedMinutes is int minutes ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;

        // Act
        var delay = _policy.GetRetryDelay(
            attempt,
            retryAfter);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            delay);
    }

    [Fact]
    public void Constructor_WhenCallerMutatesRetryArray_PreservesPolicy()
    {
        // Arrange
        var delays = new[]
        {
            TimeSpan.FromMinutes(1)
        };
        var policy = new WishlistModerationEmailDeliveryPolicy(
            20,
            TimeSpan.FromMinutes(2),
            10,
            delays,
            TimeSpan.FromHours(1),
            TimeSpan.FromDays(30));

        // Act
        delays[0] = TimeSpan.FromHours(3);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            policy.GetRetryDelay(
                1,
                null));
        Assert.Equal(
            20,
            policy.BatchSize);
        Assert.Equal(
            10,
            policy.MaximumAttempts);
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            policy.LeaseDuration);
        Assert.Equal(
            TimeSpan.FromDays(30),
            policy.ProcessedRetention);
    }
}
