using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Workers;

public class AccountErasureWorkerTests : IAsyncDisposable
{
    private readonly Mock<IAccountErasureMaintenance> _maintenanceMock;
    private readonly Mock<IAccountErasureEmailDispatcher> _dispatcherMock;
    private readonly RecordingLogger<AccountErasureWorker> _logger = new();
    private readonly ServiceProvider _provider;
    private readonly CancellationTokenSource _cancellationSource;
    private readonly CapturingTimeProvider _timeProvider;

    public AccountErasureWorkerTests()
    {
        _maintenanceMock = new Mock<IAccountErasureMaintenance>(MockBehavior.Strict);
        _dispatcherMock = new Mock<IAccountErasureEmailDispatcher>(MockBehavior.Strict);
        _cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _timeProvider = new CapturingTimeProvider(
            DateTimeOffset.UnixEpoch,
            _cancellationSource);
        var services = new ServiceCollection();
        services.AddScoped(_ => _maintenanceMock.Object);
        services.AddScoped(_ => _dispatcherMock.Object);
        _provider = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("disabled", false, false)]
    [InlineData("success", true, false)]
    [InlineData("cleanup-failure", false, true)]
    [InlineData("delivery-failure", true, true)]
    [InlineData("unrelated-cancellation", false, true)]
    [InlineData("cancellation", false, false)]
    public async Task ExecuteAsync_WhenCycleRuns_PurgesIndependentlyAndPreservesCancellation(
        string scenario,
        bool dispatches,
        bool fails)
    {
        // Arrange
        var receivedToken = CancellationToken.None;
        _maintenanceMock.Setup(maintenance => maintenance.PurgeAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(token =>
            {
                receivedToken = token;

                if (scenario == "cancellation")
                {
                    _cancellationSource.Cancel();

                    return Task.FromCanceled(token);
                }

                return scenario switch
                {
                    "cleanup-failure" => Task.FromException(new InvalidOperationException("PRIVATE /volume/recipient")),
                    "unrelated-cancellation" => Task.FromException(new OperationCanceledException("PRIVATE /volume/recipient")),
                    _ => Task.CompletedTask
                };
            });

        if (dispatches)
            _dispatcherMock.Setup(dispatcher => dispatcher.DispatchAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(token => scenario == "delivery-failure" ? Task.FromException<int>(new InvalidOperationException("PRIVATE recipient@example.test")) : Task.FromResult(1));
        using var worker = new AccountErasureWorker(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new AccountErasureProcessingOptions()),
            Microsoft.Extensions.Options.Options.Create(new AuthenticationEmailOptions
            {
                Provider = scenario == "disabled" ? AuthenticationEmailOptions.DisabledProvider : AuthenticationEmailOptions.GmailProvider
            }),
            _timeProvider,
            _logger);

        // Act
        await worker.StartAsync(_cancellationSource.Token);
        await Assert.IsType<Task>(
                worker.ExecuteTask,
                exactMatch: false)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(receivedToken.IsCancellationRequested);

        if (scenario == "cancellation")
            Assert.Null(_timeProvider.DueTime);
        else
            Assert.Equal(
                fails ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(10),
                _timeProvider.DueTime);

        if (fails)
        {
            var entry = Assert.Single(_logger.Entries);
            Assert.Equal(
                LogLevel.Error,
                entry.LogLevel);
            Assert.Equal(
                LogEventIds.AccountErasureProcessingFailed,
                entry.EventId.Id);
            Assert.Null(entry.Exception);
            Assert.DoesNotContain(
                "PRIVATE",
                entry.Message);
        }
        else
        {
            Assert.Empty(_logger.Entries);
        }

        var scope = Assert.Single(_logger.Scopes);
        Assert.NotNull(scope["TraceId"]);
        Assert.NotNull(scope["CorrelationId"]);
        _maintenanceMock.Verify(maintenance => maintenance.PurgeAsync(receivedToken), Times.Once);

        if (dispatches)
            _dispatcherMock.Verify(dispatcher => dispatcher.DispatchAsync(receivedToken), Times.Once);
        _maintenanceMock.VerifyNoOtherCalls();
        _dispatcherMock.VerifyNoOtherCalls();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        _cancellationSource.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFirstCycleCompletes_StartsAnotherCorrelatedCleanupCycle()
    {
        // Arrange
        var cycles = 0;
        var receivedToken = CancellationToken.None;
        _maintenanceMock.Setup(maintenance => maintenance.PurgeAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(token =>
            {
                receivedToken = token;
                cycles++;

                if (cycles == 1)
                    return Task.CompletedTask;
                _cancellationSource.Cancel();

                return Task.FromCanceled(token);
            });
        using var worker = new AccountErasureWorker(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new AccountErasureProcessingOptions()),
            Microsoft.Extensions.Options.Options.Create(new AuthenticationEmailOptions()),
            new ImmediateTimeProvider(DateTimeOffset.UnixEpoch),
            _logger);

        // Act
        await worker.StartAsync(_cancellationSource.Token);
        await Assert.IsType<Task>(
                worker.ExecuteTask,
                exactMatch: false)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            cycles);
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
        _maintenanceMock.Verify(maintenance => maintenance.PurgeAsync(receivedToken), Times.Exactly(2));
        _maintenanceMock.VerifyNoOtherCalls();
        _dispatcherMock.VerifyNoOtherCalls();
    }
}
