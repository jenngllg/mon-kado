using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Workers;

public class PersonalDataExportWorkerTests
{
    private readonly Mock<IPersonalDataExportJobs> _jobsMock;
    private readonly Mock<IPersonalDataExportArchiveBuilder> _builderMock;
    private readonly ControlledExportTimeProvider _clock = new();
    private readonly RecordingLogger<PersonalDataExportWorker> _logger = new();
    private readonly PersonalDataExportOptions _options = new();
    private readonly PersonalDataExportWorkItem _workItem = new()
    {
        ExportId = Guid.CreateVersion7(),
        MemberId = Guid.CreateVersion7(),
        LeaseId = Guid.CreateVersion7(),
        AttemptCount = 1
    };
    private readonly PersonalDataExportArchive _archive = new()
    {
        SnapshotAt = new DateTime(
            2026,
            9,
            7,
            12,
            0,
            0,
            DateTimeKind.Utc),
        SizeInBytes = 1024
    };
    private CancellationToken _cycleToken;
    private CancellationToken _attemptToken;
    public PersonalDataExportWorkerTests()
    {
        _jobsMock = new(MockBehavior.Strict);
        _builderMock = new(MockBehavior.Strict);
        _jobsMock
            .Setup(jobs => jobs.CleanupAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(token => _cycleToken = token)
            .Returns(Task.CompletedTask);
        _jobsMock
            .Setup(jobs => jobs.ClaimAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_workItem);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ExecuteAsync_WhenPublicationIsFenced_OnlyPublishesWithLiveOwnership(
        bool renewed,
        bool completed)
    {
        // Arrange
        _builderMock
            .Setup(builder => builder.BuildAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .Callback<PersonalDataExportWorkItem, CancellationToken>((
                _,
                token) => _attemptToken = token)
            .ReturnsAsync(_archive);
        _jobsMock
            .Setup(jobs => jobs.RenewAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(renewed);

        if (renewed)
            _jobsMock
                .Setup(jobs => jobs.CompleteAsync(
                    _workItem,
                    _archive,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(completed);

        if (!completed)
            _jobsMock
                .Setup(jobs => jobs.FailAsync(
                    _workItem,
                    PersonalDataExportFailure.GenerationFailed,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider);

        // Act
        await RunOneCycleAsync(
            worker,
            _options.PollInterval);

        // Assert
        Assert.True(_attemptToken.CanBeCanceled);
        Assert.NotEqual(
            _cycleToken,
            _attemptToken);
        VerifyCycle();
        _builderMock.Verify(
            builder => builder.BuildAsync(
                _workItem,
                _attemptToken),
            Times.Once);
        _jobsMock.Verify(
            jobs => jobs.RenewAsync(
                _workItem,
                _attemptToken),
            Times.Once);

        if (renewed)
            _jobsMock.Verify(
                jobs => jobs.CompleteAsync(
                    _workItem,
                    _archive,
                    _attemptToken),
                Times.Once);

        if (!completed)
            _jobsMock.Verify(
                jobs => jobs.FailAsync(
                    _workItem,
                    PersonalDataExportFailure.GenerationFailed,
                    _cycleToken),
                Times.Once);
        Assert.Contains(
            _logger.Entries,
            entry => entry.EventId.Id == (completed ? LogEventIds.PersonalDataExportReady : LogEventIds.PersonalDataExportAttemptFailed));
        AssertSanitizedLogs();
        _jobsMock.VerifyNoOtherCalls();
        _builderMock.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> GenerationFailures()
    {
        yield return [
            new PersonalDataExportTooLargeException(),
            PersonalDataExportFailure.TooLarge,
            "TOO_LARGE"
        ];
        yield return [
            new PersonalDataExportStorageUnavailableException(),
            PersonalDataExportFailure.GenerationFailed,
            "STORAGE_UNAVAILABLE"
        ];
        yield return [
            new IOException("/private/member/export.zip"),
            PersonalDataExportFailure.GenerationFailed,
            "STORAGE_UNAVAILABLE"
        ];
        yield return [
            new UnauthorizedAccessException("/private/member/export.zip"),
            PersonalDataExportFailure.GenerationFailed,
            "STORAGE_UNAVAILABLE"
        ];
        yield return [
            new GiftImageStorageUnavailableException(new IOException("/private/member/export.zip")),
            PersonalDataExportFailure.GenerationFailed,
            "STORAGE_UNAVAILABLE"
        ];
        yield return [
            new DependencyUnavailableException(
                "PostgreSQL",
                new IOException("/private/member/export.zip")),
            PersonalDataExportFailure.GenerationFailed,
            "DEPENDENCY_UNAVAILABLE"
        ];
        yield return [
            new InvalidAuthenticationSessionException(),
            PersonalDataExportFailure.GenerationFailed,
            "MEMBER_UNAVAILABLE"
        ];
        yield return [
            new OperationCanceledException(),
            PersonalDataExportFailure.GenerationFailed,
            "ATTEMPT_CANCELLED"
        ];
        yield return [
            new InvalidOperationException("/private/member/export.zip"),
            PersonalDataExportFailure.GenerationFailed,
            "GENERATION_FAILED"
        ];
    }

    [Theory]
    [MemberData(nameof(GenerationFailures))]
    public async Task ExecuteAsync_WhenGenerationFails_AcknowledgesBoundedFailureWithoutLoggingException(
        Exception exception,
        PersonalDataExportFailure failure,
        string classification)
    {
        // Arrange
        _builderMock
            .Setup(builder => builder.BuildAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .Callback<PersonalDataExportWorkItem, CancellationToken>((
                _,
                token) => _attemptToken = token)
            .ThrowsAsync(exception);
        _jobsMock
            .Setup(jobs => jobs.FailAsync(
                _workItem,
                failure,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider);

        // Act
        await RunOneCycleAsync(
            worker,
            _options.PollInterval);

        // Assert
        VerifyCycle();
        _builderMock.Verify(
            builder => builder.BuildAsync(
                _workItem,
                _attemptToken),
            Times.Once);
        _jobsMock.Verify(
            jobs => jobs.FailAsync(
                _workItem,
                failure,
                _cycleToken),
            Times.Once);
        var entry = Assert.Single(
            _logger.Entries,
            entry => entry.EventId.Id == LogEventIds.PersonalDataExportAttemptFailed);
        Assert.Contains(
            classification,
            entry.Message);
        AssertSanitizedLogs();
        _jobsMock.VerifyNoOtherCalls();
        _builderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenHeartbeatCannotConfirmOwnership_CancelsGenerationAndNeverPublishes(bool throws)
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<PersonalDataExportArchive>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken renewalToken = default;
        _builderMock
            .Setup(builder => builder.BuildAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .Returns<PersonalDataExportWorkItem, CancellationToken>((
                _,
                token) =>
            {
                _attemptToken = token;
                started.SetResult();

                return pending.Task.WaitAsync(token);
            });
        _jobsMock
            .Setup(jobs => jobs.RenewAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .Returns<PersonalDataExportWorkItem, CancellationToken>((
                _,
                token) =>
            {
                renewalToken = token;

                return throws ? Task.FromException<bool>(new IOException("/private/member/export.zip")) : Task.FromResult(false);
            });
        _jobsMock
            .Setup(jobs => jobs.FailAsync(
                _workItem,
                PersonalDataExportFailure.GenerationFailed,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider);

        // Act
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var timer = await _clock.WaitForTimerAsync(
            _options.LeaseRenewalInterval,
            TestContext.Current.CancellationToken);
        timer.Fire();
        await WaitForCycleAndStopAsync(
            worker,
            _options.PollInterval);

        // Assert
        Assert.True(_attemptToken.IsCancellationRequested);
        VerifyCycle();
        _builderMock.Verify(
            builder => builder.BuildAsync(
                _workItem,
                _attemptToken),
            Times.Once);
        _jobsMock.Verify(
            jobs => jobs.RenewAsync(
                _workItem,
                renewalToken),
            Times.Once);
        _jobsMock.Verify(
            jobs => jobs.FailAsync(
                _workItem,
                PersonalDataExportFailure.GenerationFailed,
                _cycleToken),
            Times.Once);
        AssertSanitizedLogs();
        _jobsMock.VerifyNoOtherCalls();
        _builderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenGenerationIsCancelled_DistinguishesDeadlineFromHostShutdown(bool hostShutdown)
    {
        // Arrange
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<PersonalDataExportArchive>(TaskCreationOptions.RunContinuationsAsynchronously);
        _builderMock
            .Setup(builder => builder.BuildAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .Returns<PersonalDataExportWorkItem, CancellationToken>((
                _,
                token) =>
            {
                _attemptToken = token;
                started.SetResult();

                return pending.Task.WaitAsync(token);
            });

        if (!hostShutdown)
            _jobsMock
                .Setup(jobs => jobs.FailAsync(
                    _workItem,
                    PersonalDataExportFailure.GenerationFailed,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider);

        // Act
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);

        if (hostShutdown)
            await worker.StopAsync(TestContext.Current.CancellationToken);
        else
        {
            var timer = await _clock.WaitForTimerAsync(
                _options.AttemptTimeout,
                TestContext.Current.CancellationToken);
            timer.Fire();
            await WaitForCycleAndStopAsync(
                worker,
                _options.PollInterval);
        }

        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(_attemptToken.IsCancellationRequested);
        VerifyCycle();
        _builderMock.Verify(
            builder => builder.BuildAsync(
                _workItem,
                _attemptToken),
            Times.Once);

        if (!hostShutdown)
            _jobsMock.Verify(
                jobs => jobs.FailAsync(
                    _workItem,
                    PersonalDataExportFailure.GenerationFailed,
                    _cycleToken),
                Times.Once);
        else
            Assert.DoesNotContain(
                _logger.Entries,
                entry => entry.EventId.Id == LogEventIds.PersonalDataExportAttemptFailed);
        _jobsMock.VerifyNoOtherCalls();
        _builderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenNoJobOrCleanupFails_UsesTheConfiguredCycleDelay(bool cleanupFails)
    {

        // Arrange
        if (cleanupFails)
            _jobsMock
                .Setup(jobs => jobs.CleanupAsync(It.IsAny<CancellationToken>()))
                .Callback<CancellationToken>(token => _cycleToken = token)
                .ThrowsAsync(new IOException("/private/member/export.zip"));
        else
            _jobsMock
                .Setup(jobs => jobs.ClaimAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((PersonalDataExportWorkItem?)null);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider);

        // Act
        await RunOneCycleAsync(
            worker,
            cleanupFails ? _options.FailureInterval : _options.PollInterval);

        // Assert
        _jobsMock.Verify(
            jobs => jobs.CleanupAsync(_cycleToken),
            Times.Once);

        if (!cleanupFails)
            _jobsMock.Verify(
                jobs => jobs.ClaimAsync(_cycleToken),
                Times.Once);
        else
            Assert.Contains(
                _logger.Entries,
                entry => entry.EventId.Id == LogEventIds.PersonalDataExportCycleFailed);
        AssertSanitizedLogs();
        _jobsMock.VerifyNoOtherCalls();
        _builderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenGenerationSpansHeartbeat_RenewsIndependentlyThenPublishes()
    {
        // Arrange
        var renewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generated = new TaskCompletionSource<PersonalDataExportArchive>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        _jobsMock
            .Setup(jobs => jobs.RenewAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .Callback<PersonalDataExportWorkItem, CancellationToken>((
                _,
                token) =>
            {
                tokens.Add(token);
                renewed.TrySetResult();
            })
            .ReturnsAsync(true);
        _jobsMock
            .Setup(jobs => jobs.CompleteAsync(
                _workItem,
                _archive,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _builderMock
            .Setup(builder => builder.BuildAsync(
                _workItem,
                It.IsAny<CancellationToken>()))
            .Returns<PersonalDataExportWorkItem, CancellationToken>((
                _,
                token) =>
            {
                _attemptToken = token;

                return generated.Task.WaitAsync(token);
            });
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider);

        // Act
        await worker.StartAsync(TestContext.Current.CancellationToken);
        var timer = await _clock.WaitForTimerAsync(
            _options.LeaseRenewalInterval,
            TestContext.Current.CancellationToken);
        timer.Fire();
        await renewed.Task.WaitAsync(TestContext.Current.CancellationToken);
        generated.SetResult(_archive);
        await WaitForCycleAndStopAsync(
            worker,
            _options.PollInterval);

        // Assert
        Assert.Equal(
            2,
            tokens.Count);
        Assert.NotEqual(
            _attemptToken,
            tokens[0]);
        Assert.Equal(
            _attemptToken,
            tokens[1]);
        VerifyCycle();
        _jobsMock.Verify(
            jobs => jobs.RenewAsync(
                _workItem,
                tokens[0]),
            Times.Once);
        _jobsMock.Verify(
            jobs => jobs.RenewAsync(
                _workItem,
                _attemptToken),
            Times.Once);
        _jobsMock.Verify(
            jobs => jobs.CompleteAsync(
                _workItem,
                _archive,
                _attemptToken),
            Times.Once);
        _builderMock.Verify(
            builder => builder.BuildAsync(
                _workItem,
                _attemptToken),
            Times.Once);
        _jobsMock.VerifyNoOtherCalls();
        _builderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPollTimerTicks_StartsAnotherBoundedCycle()
    {
        // Arrange
        var repeated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        _jobsMock
            .Setup(jobs => jobs.ClaimAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {

                if (++count == 2)
                    repeated.TrySetResult();

                return (PersonalDataExportWorkItem?)null;
            });
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider);

        // Act
        await worker.StartAsync(TestContext.Current.CancellationToken);
        var timer = await _clock.WaitForTimerAsync(
            _options.PollInterval,
            TestContext.Current.CancellationToken);
        timer.Fire();
        await repeated.Task.WaitAsync(TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        _jobsMock.Verify(
            jobs => jobs.CleanupAsync(_cycleToken),
            Times.Exactly(2));
        _jobsMock.Verify(
            jobs => jobs.ClaimAsync(_cycleToken),
            Times.Exactly(2));
        _jobsMock.VerifyNoOtherCalls();
        _builderMock.VerifyNoOtherCalls();
    }

    private ServiceProvider CreateProvider()
    {

        return new ServiceCollection()
            .AddScoped(_ => _jobsMock.Object)
            .AddScoped(_ => _builderMock.Object)
            .BuildServiceProvider();
    }

    private PersonalDataExportWorker CreateWorker(ServiceProvider provider)
    {

        return new PersonalDataExportWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _clock,
            Microsoft.Extensions.Options.Options.Create(_options),
            _logger);
    }

    private async Task RunOneCycleAsync(
        PersonalDataExportWorker worker,
        TimeSpan delay)
    {
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitForCycleAndStopAsync(
            worker,
            delay);
    }

    private async Task WaitForCycleAndStopAsync(
        PersonalDataExportWorker worker,
        TimeSpan delay)
    {
        await _clock.WaitForTimerAsync(
            delay,
            TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask.WaitAsync(TestContext.Current.CancellationToken);
    }

    private void VerifyCycle()
    {
        _jobsMock.Verify(
            jobs => jobs.CleanupAsync(_cycleToken),
            Times.Once);
        _jobsMock.Verify(
            jobs => jobs.ClaimAsync(_cycleToken),
            Times.Once);
    }

    private void AssertSanitizedLogs()
    {
        Assert.All(
            _logger.Entries,
            entry =>
            {
                Assert.Null(entry.Exception);
                Assert.DoesNotContain(
                    "/private/",
                    entry.Message);
            });
        Assert.All(
            _logger.Scopes,
            scope =>
            {
                Assert.True(scope.ContainsKey("CorrelationId"));
                Assert.True(scope.ContainsKey("TraceId"));
            });
    }
}
