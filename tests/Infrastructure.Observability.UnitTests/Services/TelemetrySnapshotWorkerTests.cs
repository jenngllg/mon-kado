using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Services;

public class TelemetrySnapshotWorkerTests
{
    private readonly Mock<ITelemetrySnapshotSource> _sourceMock = new(MockBehavior.Strict);
    private readonly Mock<ITelemetrySnapshotStore> _storeMock = new(MockBehavior.Strict);
    private readonly Mock<TimeProvider> _clockMock = new(MockBehavior.Strict);
    private readonly Mock<ITimer> _timerMock = new(MockBehavior.Strict);
    private readonly TaskCompletionSource<(TimerCallback Callback, object? State)> _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ApplicationTelemetrySnapshot _snapshot = new();

    public TelemetrySnapshotWorkerTests()
    {
        _clockMock.Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30)))
            .Returns((TimerCallback callback, object? state, TimeSpan _, TimeSpan _) =>
            {
                _timerCreated.SetResult((callback, state));

                return _timerMock.Object;
            });
        _timerMock.Setup(timer => timer.Dispose());
    }

    [Fact]
    public async Task StartAsync_WhenDisabled_DoesNotCaptureOrCreateTimer()
    {
        // Arrange
        using var worker = CreateWorker(false);

        // Act
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await Execution(worker).WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Execution(worker).IsCompletedSuccessfully);
        _sourceMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
        _clockMock.VerifyNoOtherCalls();
        _timerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartAsync_WhenTimerTicks_PublishesAgainWithTheSameHostCancellationToken()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var firstWrite = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWrite = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sourceMock.Setup(source => source.Capture())
            .Returns(_snapshot);
        _storeMock.Setup(store => store.WriteAsync(
                _snapshot,
                It.IsAny<CancellationToken>()))
            .Returns((ApplicationTelemetrySnapshot _, CancellationToken token) =>
            {

                if (!firstWrite.TrySetResult(token))
                    secondWrite.SetResult(token);

                return Task.CompletedTask;
            });
        using var worker = CreateWorker(true);

        // Act
        await worker.StartAsync(cancellation.Token);
        var firstToken = await firstWrite.Task.WaitAsync(TestContext.Current.CancellationToken);
        var timer = await _timerCreated.Task.WaitAsync(TestContext.Current.CancellationToken);
        timer.Callback(timer.State);
        var secondToken = await secondWrite.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Execution(worker).WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(firstToken.CanBeCanceled);
        Assert.True(firstToken.IsCancellationRequested);
        Assert.Equal(
            firstToken,
            secondToken);
        _sourceMock.Verify(source => source.Capture(), Times.Exactly(2));
        _storeMock.Verify(store => store.WriteAsync(
            _snapshot,
            firstToken), Times.Exactly(2));
        VerifyTimer();
        _sourceMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("io")]
    [InlineData("permission")]
    [InlineData("invalid")]
    public async Task StartAsync_WhenPublicationFails_LogsOnlySafeEventAndTriesNextTick(string failure)
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loggerMock = new Mock<ILogger<TelemetrySnapshotWorker>>(MockBehavior.Strict);
        loggerMock.Setup(logger => logger.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Returns((IDisposable?)null);
        loggerMock.Setup(logger => logger.IsEnabled(LogLevel.Error))
            .Returns(true);
        loggerMock.Setup(logger => logger.Log(
                LogLevel.Error,
                It.Is<EventId>(id => id.Id == LogEventIds.TelemetrySnapshotFailed),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => failed.SetResult());
        Exception exception = failure switch
        {
            "io" => new IOException("PRIVATE_PATH"),
            "permission" => new UnauthorizedAccessException("PRIVATE_PATH"),
            _ => new InvalidOperationException("PRIVATE_PATH")
        };
        _sourceMock.Setup(source => source.Capture())
            .Returns(_snapshot);
        _storeMock.SetupSequence(store => store.WriteAsync(
                _snapshot,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception)
            .Returns(() =>
            {
                completed.SetResult();

                return Task.CompletedTask;
            });
        using var worker = CreateWorker(
            true,
            loggerMock.Object);

        // Act
        await worker.StartAsync(cancellation.Token);
        await failed.Task.WaitAsync(TestContext.Current.CancellationToken);
        var timer = await _timerCreated.Task.WaitAsync(TestContext.Current.CancellationToken);
        timer.Callback(timer.State);
        await completed.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await Execution(worker).WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        _sourceMock.Verify(source => source.Capture(), Times.Exactly(2));
        _storeMock.Verify(store => store.WriteAsync(
            _snapshot,
            It.Is<CancellationToken>(token => token.IsCancellationRequested)), Times.Exactly(2));
        loggerMock.Verify(logger => logger.BeginScope(It.Is<Dictionary<string, object>>(scope =>
            scope.Count == 2 && scope.ContainsKey("CorrelationId") && scope.ContainsKey("TraceId"))), Times.Exactly(2));
        loggerMock.Verify(logger => logger.IsEnabled(LogLevel.Error), Times.Once);
        loggerMock.Verify(logger => logger.Log(
            LogLevel.Error,
            It.Is<EventId>(id => id.Id == LogEventIds.TelemetrySnapshotFailed),
            It.IsAny<It.IsAnyType>(),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        VerifyTimer();
        _sourceMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
        loggerMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_WhenUnexpectedFailure_DoesNotPretendPublicationSucceeded(bool cancellation)
    {
        // Arrange
        Exception exception = cancellation
            ? new OperationCanceledException()
            : new ArgumentException("Unexpected test failure.");
        _sourceMock.Setup(source => source.Capture())
            .Throws(exception);
        using var worker = CreateWorker(true);

        // Act
        await worker.StartAsync(TestContext.Current.CancellationToken);
        var actual = await Record.ExceptionAsync(() => Execution(worker).WaitAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(
            exception,
            actual);
        _sourceMock.Verify(source => source.Capture(), Times.Once);
        VerifyTimer();
        _sourceMock.VerifyNoOtherCalls();
        _storeMock.VerifyNoOtherCalls();
    }

    private TelemetrySnapshotWorker CreateWorker(
        bool enabled,
        ILogger<TelemetrySnapshotWorker>? logger = null)
    {

        return new TelemetrySnapshotWorker(
            _sourceMock.Object,
            _storeMock.Object,
            _clockMock.Object,
            Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions { Enabled = enabled }),
            logger ?? NullLogger<TelemetrySnapshotWorker>.Instance);
    }

    private void VerifyTimer()
    {
        _clockMock.Verify(clock => clock.CreateTimer(
            It.IsAny<TimerCallback>(),
            It.IsAny<object?>(),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30)), Times.Once);
        _timerMock.Verify(timer => timer.Dispose(), Times.Once);
        _clockMock.VerifyNoOtherCalls();
        _timerMock.VerifyNoOtherCalls();
    }

    private static Task Execution(TelemetrySnapshotWorker worker)
    {
        Assert.NotNull(worker.ExecuteTask);

        return worker.ExecuteTask;
    }
}
