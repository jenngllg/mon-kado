using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Workers;

public class WishlistModerationEmailWorkerTests : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationSource;
    private readonly Mock<IWishlistModerationEmailDispatcher> _dispatcherMock;
    private readonly RecordingLogger<WishlistModerationEmailWorker> _logger;
    private readonly ServiceProvider _provider;
    private readonly CapturingTimeProvider _timeProvider;
    private readonly WishlistModerationEmailWorker _worker;
    public WishlistModerationEmailWorkerTests()
    {
        _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _dispatcherMock = new Mock<IWishlistModerationEmailDispatcher>(MockBehavior.Strict);
        _logger = new RecordingLogger<WishlistModerationEmailWorker>();
        var services = new ServiceCollection();
        services.AddScoped(_ => _dispatcherMock.Object);
        _provider = services.BuildServiceProvider();
        _timeProvider = new CapturingTimeProvider(
            DateTimeOffset.UnixEpoch,
            _cancellationSource);
        _worker = CreateWorker(
            _timeProvider,
            AuthenticationEmailOptions.GmailProvider);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmailIsDisabled_DoesNotResolveDispatcher()
    {
        // Arrange
        using var worker = CreateWorker(
            _timeProvider,
            AuthenticationEmailOptions.DisabledProvider);

        // Act
        await worker.StartAsync(_cancellationSource.Token);
        await Assert
            .IsAssignableFrom<Task>(worker.ExecuteTask)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(_timeProvider.DueTime);
        Assert.Empty(_logger.Entries);
        Assert.Empty(_logger.Scopes);
        _dispatcherMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCycleSucceeds_UsesConfiguredPolicyAndPollInterval()
    {
        // Arrange
        WishlistModerationEmailDeliveryPolicy? receivedPolicy = null;
        var receivedToken = CancellationToken.None;
        _dispatcherMock
            .Setup(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<WishlistModerationEmailDeliveryPolicy>(),
                It.IsAny<CancellationToken>()))
            .Callback<WishlistModerationEmailDeliveryPolicy, CancellationToken>((
                policy,
                token) =>
            {
                receivedPolicy = policy;
                receivedToken = token;
            })
            .ReturnsAsync(1);

        // Act
        await _worker.StartAsync(_cancellationSource.Token);
        await Assert
            .IsAssignableFrom<Task>(_worker.ExecuteTask)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        var policy = Assert.IsType<WishlistModerationEmailDeliveryPolicy>(receivedPolicy);
        Assert.Equal(
            20,
            policy.BatchSize);
        Assert.Equal(
            10,
            policy.MaximumAttempts);
        Assert.Equal(
            TimeSpan.FromDays(30),
            policy.ProcessedRetention);
        Assert.Equal(
            TimeSpan.FromMinutes(2),
            policy.LeaseDuration);
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            _timeProvider.DueTime);
        Assert.True(receivedToken.IsCancellationRequested);
        Assert.Empty(_logger.Entries);
        var scope = Assert.Single(_logger.Scopes);
        Assert.NotNull(scope["TraceId"]);
        Assert.NotNull(scope["CorrelationId"]);
        _dispatcherMock.Verify(
            dispatcher => dispatcher.DispatchAsync(
                policy,
                receivedToken),
            Times.Once);
        _dispatcherMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenCycleFails_UsesFailureIntervalWithoutPrivateException(bool isUnrelatedCancellation)
    {
        // Arrange
        var failure = isUnrelatedCancellation ? (Exception)new OperationCanceledException("PRIVATE recipient@example.test") : new InvalidOperationException("PRIVATE /volume/secret");
        var receivedToken = CancellationToken.None;
        _dispatcherMock
            .Setup(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<WishlistModerationEmailDeliveryPolicy>(),
                It.IsAny<CancellationToken>()))
            .Callback<WishlistModerationEmailDeliveryPolicy, CancellationToken>((
                _,
                token) => receivedToken = token)
            .ThrowsAsync(failure);

        // Act
        await _worker.StartAsync(_cancellationSource.Token);
        await Assert
            .IsAssignableFrom<Task>(_worker.ExecuteTask)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            _timeProvider.DueTime);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(
            LogLevel.Error,
            entry.LogLevel);
        Assert.Equal(
            LogEventIds.WishlistModerationEmailCycleFailed,
            entry.EventId.Id);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(
            "PRIVATE",
            entry.Message);
        var scope = Assert.Single(_logger.Scopes);
        Assert.Equal(
            scope["TraceId"],
            Assert.Single(_logger.TraceIdsAtLog));
        _dispatcherMock.Verify(
            dispatcher => dispatcher.DispatchAsync(
                It.IsAny<WishlistModerationEmailDeliveryPolicy>(),
                receivedToken),
            Times.Once);
        _dispatcherMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHostStopsDuringDelivery_ExitsWithoutFailureLog()
    {
        // Arrange
        var receivedToken = CancellationToken.None;
        _dispatcherMock
            .Setup(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<WishlistModerationEmailDeliveryPolicy>(),
                It.IsAny<CancellationToken>()))
            .Returns<WishlistModerationEmailDeliveryPolicy, CancellationToken>((
                _,
                token) =>
            {
                receivedToken = token;
                _cancellationSource.Cancel();

                return Task.FromCanceled<int>(token);
            });

        // Act
        await _worker.StartAsync(_cancellationSource.Token);
        await Assert
            .IsAssignableFrom<Task>(_worker.ExecuteTask)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(receivedToken.IsCancellationRequested);
        Assert.Null(_timeProvider.DueTime);
        Assert.Empty(_logger.Entries);
        _dispatcherMock.Verify(
            dispatcher => dispatcher.DispatchAsync(
                It.IsAny<WishlistModerationEmailDeliveryPolicy>(),
                receivedToken),
            Times.Once);
        _dispatcherMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDelayCompletes_StartsFreshCorrelatedCycle()
    {
        // Arrange
        var attempts = 0;
        var receivedToken = CancellationToken.None;
        _dispatcherMock
            .Setup(dispatcher => dispatcher.DispatchAsync(
                It.IsAny<WishlistModerationEmailDeliveryPolicy>(),
                It.IsAny<CancellationToken>()))
            .Returns<WishlistModerationEmailDeliveryPolicy, CancellationToken>((
                _,
                token) =>
            {
                receivedToken = token;
                attempts++;

                if (attempts == 1)
                    return Task.FromResult(0);
                _cancellationSource.Cancel();

                return Task.FromCanceled<int>(token);
            });
        using var worker = CreateWorker(
            new ImmediateTimeProvider(DateTimeOffset.UnixEpoch),
            AuthenticationEmailOptions.GmailProvider);

        // Act
        await worker.StartAsync(_cancellationSource.Token);
        await Assert
            .IsAssignableFrom<Task>(worker.ExecuteTask)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            attempts);
        Assert.Equal(
            2,
            _logger.Scopes.Count);
        Assert.NotEqual(
            _logger.Scopes[0]["CorrelationId"],
            _logger.Scopes[1]["CorrelationId"]);
        Assert.NotEqual(
            _logger.Scopes[0]["TraceId"],
            _logger.Scopes[1]["TraceId"]);
        Assert.Empty(_logger.Entries);
        _dispatcherMock.Verify(
            dispatcher => dispatcher.DispatchAsync(
                It.IsAny<WishlistModerationEmailDeliveryPolicy>(),
                receivedToken),
            Times.Exactly(2));
        _dispatcherMock.VerifyNoOtherCalls();
    }

    public async ValueTask DisposeAsync()
    {
        _worker.Dispose();
        await _provider.DisposeAsync();
        _cancellationSource.Dispose();
        GC.SuppressFinalize(this);
    }

    private WishlistModerationEmailWorker CreateWorker(
        TimeProvider timeProvider,
        string provider)
    {

        return new WishlistModerationEmailWorker(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new WishlistModerationEmailOptions()),
            Microsoft.Extensions.Options.Options.Create(new AuthenticationEmailOptions { Provider = provider }),
            timeProvider,
            _logger);
    }
}
