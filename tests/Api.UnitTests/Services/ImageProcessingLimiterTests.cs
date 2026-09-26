using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class ImageProcessingLimiterTests
{
    [Fact]
    public async Task AcquireAsync_WhenQueueIsFull_RejectsAndPreservesFifoOrder()
    {
        // Arrange
        using var limiter = new ImageProcessingLimiter(new FrozenTimerTimeProvider());
        using var first = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        var secondTask = limiter.AcquireAsync(TestContext.Current.CancellationToken);
        var thirdTask = limiter.AcquireAsync(TestContext.Current.CancellationToken);

        // Act
        using var rejected = await limiter.AcquireAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(first);
        Assert.NotNull(rejected);
        Assert.True(first.IsAcquired);
        Assert.False(rejected.IsAcquired);
        Assert.False(secondTask.IsCompleted);
        Assert.False(thirdTask.IsCompleted);
        first.Dispose();
        using var second = await secondTask;
        Assert.NotNull(second);
        Assert.True(second.IsAcquired);
        Assert.False(thirdTask.IsCompleted);
        second.Dispose();
        using var third = await thirdTask;
        Assert.NotNull(third);
        Assert.True(third.IsAcquired);
    }

    [Fact]
    public async Task AcquireAsync_WhenWaitExpires_ReturnsNullAndRemovesQueuedRequest()
    {
        // Arrange
        var clockMock = new Mock<TimeProvider>(MockBehavior.Strict);
        var timerMock = new Mock<ITimer>(MockBehavior.Strict);
        TimerCallback? expire = null;
        object? timerState = null;
        timerMock.Setup(timer => timer.Dispose());
        clockMock.Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                TimeSpan.FromSeconds(5),
                Timeout.InfiniteTimeSpan))
            .Callback<TimerCallback, object?, TimeSpan, TimeSpan>((
                callback,
                state,
                _,
                _) =>
            {
                expire = callback;
                timerState = state;
            })
            .Returns(timerMock.Object);
        using var limiter = new ImageProcessingLimiter(clockMock.Object);
        using var first = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        var waiting = limiter.AcquireAsync(TestContext.Current.CancellationToken);

        // Act
        Assert.NotNull(expire);
        expire(timerState);
        using var expired = await waiting;
        Assert.NotNull(first);
        first.Dispose();
        using var subsequent = await limiter.AcquireAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(expired);
        Assert.NotNull(subsequent);
        Assert.True(subsequent.IsAcquired);
        clockMock.Verify(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                TimeSpan.FromSeconds(5),
                Timeout.InfiniteTimeSpan),
            Times.Exactly(3));
        timerMock.Verify(
            timer => timer.Dispose(),
            Times.Exactly(3));
        clockMock.VerifyNoOtherCalls();
        timerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AcquireAsync_WhenCallerCancels_PropagatesCancellationAndFreesQueueSlot()
    {
        // Arrange
        using var limiter = new ImageProcessingLimiter(new FrozenTimerTimeProvider());
        using var first = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var waiting = limiter.AcquireAsync(cancellation.Token);

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        Assert.NotNull(first);
        first.Dispose();
        using var subsequent = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(subsequent);
        Assert.True(subsequent.IsAcquired);
    }

    [Fact]
    public async Task Dispose_WhenCallersAreQueued_RejectsQueuedAdmission()
    {
        // Arrange
        using var limiter = new ImageProcessingLimiter(new FrozenTimerTimeProvider());
        using var first = await limiter.AcquireAsync(TestContext.Current.CancellationToken);
        var waiting = limiter.AcquireAsync(TestContext.Current.CancellationToken);

        // Act
        limiter.Dispose();
        using var rejected = await waiting;

        // Assert
        Assert.NotNull(rejected);
        Assert.False(rejected.IsAcquired);
    }
}
