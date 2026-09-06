using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class GiftImageCleanupWorkerTests
{
    private static readonly DateTimeOffset _now = new(
        2026,
        9,
        5,
        12,
        0,
        0,
        TimeSpan.Zero);
    [Fact]
    public async Task ExecuteAsync_WhenWorkExists_DeletesObsoleteAndReconcilesPendingImages()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cancellationToken = source.Token;
        var deletion = new GiftImageDeletion(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1);
        var referencedPending = new PendingGiftImage(
            Guid.CreateVersion7(),
            _now.UtcDateTime.AddHours(-2));
        var abandonedPending = new PendingGiftImage(
            Guid.CreateVersion7(),
            _now.UtcDateTime.AddHours(-3));
        var deletionQueue = new Queue<GiftImageDeletion?>([
                deletion,
                null
            ]);
        var cleanupMock = new Mock<IGiftImageCleanupService>(MockBehavior.Strict);
        var storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        cleanupMock
            .Setup(service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => deletionQueue.Dequeue());
        storeMock
            .Setup(store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        cleanupMock
            .Setup(service => service.CompleteAsync(
                deletion.Id,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeMock
            .Setup(store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                referencedPending,
                abandonedPending
            ]);
        storeMock
            .Setup(store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        cleanupMock
            .Setup(service => service.IsReferencedAsync(
                referencedPending.ImageId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeMock
            .Setup(store => store.MarkCommittedAsync(
                referencedPending.ImageId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        cleanupMock
            .Setup(service => service.IsReferencedAsync(
                abandonedPending.ImageId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        storeMock
            .Setup(store => store.DeleteAsync(
                abandonedPending.ImageId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider(cleanupMock.Object);
        var timeProvider = new CapturingTimeProvider(
            _now,
            source);
        var logger = new RecordingLogger<GiftImageCleanupWorker>();
        var worker = CreateWorker(
            provider,
            storeMock.Object,
            timeProvider,
            logger);

        // Act
        await worker.StartAsync(cancellationToken);
        await GetExecuteTask(worker)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            timeProvider.DueTime);
        cleanupMock.Verify(
            service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        cleanupMock.Verify(
            service => service.CompleteAsync(
                deletion.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        cleanupMock.Verify(
            service => service.IsReferencedAsync(
                referencedPending.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        cleanupMock.Verify(
            service => service.IsReferencedAsync(
                abandonedPending.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.MarkCommittedAsync(
                referencedPending.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.DeleteAsync(
                abandonedPending.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Id == LogEventIds.GiftImageDeleted);
        Assert.Equal(
            2,
            logger.Entries.Count(entry => entry.EventId.Id == LogEventIds.PendingGiftImageReconciled));
        cleanupMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(30, 60)]
    public async Task ExecuteAsync_WhenDeletionFails_ReschedulesWithBoundedExponentialDelay(
        int attemptCount,
        int expectedDelayMinutes)
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cancellationToken = source.Token;
        var deletion = new GiftImageDeletion(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            attemptCount);
        var workerToken = CancellationToken.None;
        var storeToken = CancellationToken.None;
        var retryToken = CancellationToken.None;
        var cleanupMock = new Mock<IGiftImageCleanupService>(MockBehavior.Strict);
        var storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        cleanupMock
            .Setup(service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, TimeSpan, CancellationToken>((
                _,
                _,
                token) => workerToken = token)
            .ReturnsAsync(deletion);
        storeMock
            .Setup(store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((
                _,
                token) => storeToken = token)
            .ThrowsAsync(attemptCount == 1 ? new GiftImageStorageUnavailableException(new IOException("private-storage-path")) : new IOException());
        cleanupMock
            .Setup(service => service.ScheduleRetryAsync(
                deletion.Id,
                _now.UtcDateTime.AddMinutes(expectedDelayMinutes),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime, CancellationToken>((
                _,
                _,
                token) => retryToken = token)
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider(cleanupMock.Object);
        var timeProvider = new CapturingTimeProvider(
            _now,
            source);
        var logger = new RecordingLogger<GiftImageCleanupWorker>();
        var worker = CreateWorker(
            provider,
            storeMock.Object,
            timeProvider,
            logger);

        // Act
        await worker.StartAsync(cancellationToken);
        await GetExecuteTask(worker)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            timeProvider.DueTime);
        cleanupMock.Verify(
            service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()),
            Times.Once);
        cleanupMock.Verify(
            service => service.ScheduleRetryAsync(
                deletion.Id,
                _now.UtcDateTime.AddMinutes(expectedDelayMinutes),
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(
            workerToken,
            storeToken);
        Assert.Equal(
            workerToken,
            retryToken);
        var entry = Assert.Single(logger.Entries);
        Assert.DoesNotContain(
            "private-storage-path",
            entry.Message,
            StringComparison.Ordinal);

        if (attemptCount == 1)
            Assert.Null(entry.Exception);
        Assert.Equal(
            LogEventIds.GiftImageCleanupFailed,
            entry.EventId.Id);
        cleanupMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenHostStopsDuringDeletion_CompletesWithoutRetry()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cancellationToken = source.Token;
        var deletion = new GiftImageDeletion(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1);
        var cleanupMock = new Mock<IGiftImageCleanupService>(MockBehavior.Strict);
        var storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        cleanupMock
            .Setup(service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletion);
        storeMock
            .Setup(store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()))
            .Callback(source.Cancel)
            .ThrowsAsync(new OperationCanceledException());
        await using var provider = CreateProvider(cleanupMock.Object);
        var logger = new RecordingLogger<GiftImageCleanupWorker>();
        var worker = CreateWorker(
            provider,
            storeMock.Object,
            new FixedTimeProvider(_now),
            logger);

        // Act
        await worker.StartAsync(cancellationToken);
        await GetExecuteTask(worker)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logger.Entries);
        cleanupMock.Verify(
            service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        cleanupMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDeletionBatchIsFull_StopsAtConfiguredBatchSize()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cancellationToken = source.Token;
        var deletion = new GiftImageDeletion(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1);
        var cleanupMock = new Mock<IGiftImageCleanupService>(MockBehavior.Strict);
        var storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        cleanupMock
            .Setup(service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletion);
        storeMock
            .Setup(store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        cleanupMock
            .Setup(service => service.CompleteAsync(
                deletion.Id,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeMock
            .Setup(store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock
            .Setup(store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                1,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider(cleanupMock.Object);
        var timeProvider = new CapturingTimeProvider(
            _now,
            source);
        var logger = new RecordingLogger<GiftImageCleanupWorker>();
        var worker = CreateWorker(
            provider,
            storeMock.Object,
            timeProvider,
            logger,
            new GiftImageCleanupOptions { BatchSize = 1 });

        // Act
        await worker.StartAsync(cancellationToken);
        await GetExecuteTask(worker)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        cleanupMock.Verify(
            service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()),
            Times.Once);
        cleanupMock.Verify(
            service => service.CompleteAsync(
                deletion.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.DeleteAsync(
                deletion.ImageId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                1,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                1,
                It.IsAny<CancellationToken>()),
            Times.Once);
        cleanupMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDelayCompletes_StartsNextCleanupCycle()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cancellationToken = source.Token;
        var claimCount = 0;
        var cleanupMock = new Mock<IGiftImageCleanupService>(MockBehavior.Strict);
        var storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        cleanupMock
            .Setup(service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                claimCount++;

                if (claimCount == 1)
                    return Task.FromResult<GiftImageDeletion?>(null);
                source.Cancel();

                return Task.FromException<GiftImageDeletion?>(new OperationCanceledException());
            });
        storeMock
            .Setup(store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock
            .Setup(store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider(cleanupMock.Object);
        var logger = new RecordingLogger<GiftImageCleanupWorker>();
        var worker = CreateWorker(
            provider,
            storeMock.Object,
            new ImmediateTimeProvider(_now),
            logger);

        // Act
        await worker.StartAsync(cancellationToken);
        await GetExecuteTask(worker)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            claimCount);
        cleanupMock.Verify(
            service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        storeMock.Verify(
            store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()),
            Times.Once);
        storeMock.Verify(
            store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()),
            Times.Once);
        cleanupMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenTemporaryCleanupFails_RetriesWithoutLoggingStoragePaths()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanupMock = new Mock<IGiftImageCleanupService>(MockBehavior.Strict);
        var storeMock = new Mock<IGiftImageStore>(MockBehavior.Strict);
        var workerToken = CancellationToken.None;
        var temporaryToken = CancellationToken.None;
        var attempts = 0;
        cleanupMock
            .Setup(service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                It.IsAny<CancellationToken>()))
            .Callback<DateTime, TimeSpan, CancellationToken>((
                _,
                _,
                token) => workerToken = token)
            .ReturnsAsync((GiftImageDeletion?)null);
        storeMock
            .Setup(store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock
            .Setup(store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                It.IsAny<CancellationToken>()))
            .Returns<DateTime, int, CancellationToken>((
                _,
                _,
                token) =>
            {
                temporaryToken = token;
                attempts++;

                if (attempts == 1)
                    return Task.FromException(new GiftImageStorageUnavailableException(new IOException("private-storage-path")));
                source.Cancel();

                return Task.FromCanceled(token);
            });
        await using var provider = CreateProvider(cleanupMock.Object);
        var logger = new RecordingLogger<GiftImageCleanupWorker>();
        var worker = CreateWorker(
            provider,
            storeMock.Object,
            new ImmediateTimeProvider(_now),
            logger);

        // Act
        await worker.StartAsync(source.Token);
        await GetExecuteTask(worker)
            .WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            workerToken,
            temporaryToken);
        Assert.Equal(
            2,
            attempts);
        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Equal(
            logger.Scopes[0]["TraceId"],
            Assert.Single(logger.TraceIdsAtLog));
        Assert.DoesNotContain(
            "private-storage-path",
            entry.Message,
            StringComparison.Ordinal);
        cleanupMock.Verify(
            service => service.ClaimNextAsync(
                _now.UtcDateTime,
                TimeSpan.FromMinutes(5),
                workerToken),
            Times.Exactly(2));
        storeMock.Verify(
            store => store.GetPendingAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                workerToken),
            Times.Exactly(2));
        storeMock.Verify(
            store => store.CleanupTemporaryAsync(
                _now.UtcDateTime.AddHours(-1),
                100,
                workerToken),
            Times.Exactly(2));
        cleanupMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    private static ServiceProvider CreateProvider(IGiftImageCleanupService cleanupService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cleanupService);

        return services.BuildServiceProvider();
    }

    private static GiftImageCleanupWorker CreateWorker(
        ServiceProvider provider,
        IGiftImageStore store,
        TimeProvider timeProvider,
        RecordingLogger<GiftImageCleanupWorker> logger,
        GiftImageCleanupOptions? options = null)
    {

        return new GiftImageCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            store,
            timeProvider,
            Microsoft.Extensions.Options.Options.Create(options ?? new GiftImageCleanupOptions()),
            logger);
    }

    private static Task GetExecuteTask(BackgroundService worker)
    {
        var executeTask = worker.ExecuteTask;
        Assert.NotNull(executeTask);

        return executeTask;
    }
}
