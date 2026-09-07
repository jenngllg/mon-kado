using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistModerationEmailDispatcherTests
{
    private readonly Mock<TimeProvider> _clockMock;
    private readonly WishlistModerationEmailDispatcher _dispatcher;
    private readonly RecordingExceptionLogger<WishlistModerationEmailDispatcher> _logger;
    private readonly WishlistModerationEmailMessage _message;
    private readonly WishlistModerationEmailDeliveryPolicy _policy;
    private readonly Mock<IWishlistModerationEmailRepository> _repositoryMock;
    private readonly Mock<IWishlistModerationEmailSender> _senderMock;
    private readonly Mock<ITimer> _timerMock;
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private Action? _expireLease;
    public WishlistModerationEmailDispatcherTests()
    {
        _clockMock = new Mock<TimeProvider>(MockBehavior.Strict);
        _repositoryMock = new Mock<IWishlistModerationEmailRepository>(MockBehavior.Strict);
        _senderMock = new Mock<IWishlistModerationEmailSender>(MockBehavior.Strict);
        _timerMock = new Mock<ITimer>(MockBehavior.Strict);
        _logger = new RecordingExceptionLogger<WishlistModerationEmailDispatcher>();
        _message = WishlistModerationTestData.CreateSuspensionEmail();
        _policy = new WishlistModerationEmailDeliveryPolicy(
            1,
            TimeSpan.FromMinutes(2),
            10,
            [TimeSpan.FromMinutes(1)],
            TimeSpan.FromHours(1),
            TimeSpan.FromDays(30));
        _clockMock
            .Setup(clock => clock.GetUtcNow())
            .Returns(() => _now);
        _dispatcher = new WishlistModerationEmailDispatcher(
            _repositoryMock.Object,
            _senderMock.Object,
            _clockMock.Object,
            _logger);
    }

    [Theory]
    [InlineData("empty", 0, 2)]
    [InlineData("exhausted", 1, 3)]
    [InlineData("missing", 1, 3)]
    [InlineData("expired", 1, 4)]
    public async Task DispatchAsync_WhenNoDeliveryIsAllowed_DoesNotCallProvider(
        string scenario,
        int expectedProcessed,
        int expectedClockReads)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var claim = scenario == "empty" ? null : CreateClaim(scenario == "exhausted" ? 11 : 1);
        SetupClaim(
            claim,
            cancellationToken);

        if (claim is not null && scenario != "exhausted")
        {
            _repositoryMock
                .Setup(repository => repository.GetMessageAsync(
                    claim,
                    DateTime.UnixEpoch,
                    cancellationToken))
                .Callback(() => _now = DateTimeOffset.UnixEpoch.AddMinutes(2))
                .ReturnsAsync(scenario == "missing" ? null : _message);
        }

        var failure = scenario == "exhausted" ? "AttemptsExhausted" : "ResourceUnavailable";

        if (claim is not null && scenario is "missing" or "exhausted")
            SetupCompletion(
                claim,
                DateTime.UnixEpoch,
                true,
                DateTime.UnixEpoch,
                failure,
                cancellationToken);

        // Act
        var processed = await _dispatcher.DispatchAsync(
            _policy,
            cancellationToken);

        // Assert
        Assert.Equal(
            expectedProcessed,
            processed);
        VerifyClaim(cancellationToken);

        if (claim is not null && scenario != "exhausted")
            _repositoryMock.Verify(
                repository => repository.GetMessageAsync(
                    claim,
                    DateTime.UnixEpoch,
                    cancellationToken),
                Times.Once);

        if (claim is not null && scenario is "missing" or "exhausted")
            VerifyCompletion(
                claim,
                DateTime.UnixEpoch,
                true,
                DateTime.UnixEpoch,
                failure,
                cancellationToken);
        Assert.Empty(_logger.Entries);
        VerifyNoOtherCalls(expectedClockReads);
    }

    [Theory]
    [InlineData("success", 1, true, null, 0)]
    [InlineData("provider", 1, false, "RateLimited", 3)]
    [InlineData("unexpected", 1, false, "Unexpected", 1)]
    [InlineData("timeout", 1, false, "Transient", 1)]
    [InlineData("lease", 1, false, "Transient", 1)]
    [InlineData("provider", 10, true, "RateLimited", 0)]
    public async Task DispatchAsync_WhenProviderCompletes_RecordsFencedResultAndSanitizedLogs(
        string scenario,
        int attemptCount,
        bool isTerminal,
        string? expectedFailure,
        int retryMinutes)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var claim = CreateClaim(attemptCount);
        SetupDelivery(
            claim,
            cancellationToken);
        var providerToken = CancellationToken.None;
        _senderMock
            .Setup(sender => sender.SendAsync(
                _message,
                It.IsAny<CancellationToken>()))
            .Returns<WishlistModerationEmailMessage, CancellationToken>((
                _,
                token) =>
            {
                providerToken = token;

                if (scenario == "lease")
                {
                    _now = DateTimeOffset.UnixEpoch.AddMinutes(2);
                    Assert.IsType<Action>(_expireLease)();

                    return Task.FromCanceled(token);
                }

                return scenario switch
                {
                    "success" => Task.CompletedTask,
                    "provider" => Task.FromException(new WishlistModerationEmailDeliveryException(
                            WishlistModerationEmailFailure.RateLimited,
                            TimeSpan.FromMinutes(3))),
                    "timeout" => Task.FromException(new OperationCanceledException("PRIVATE recipient@example.test")),
                    _ => Task.FromException(new InvalidOperationException("PRIVATE /storage/secret"))
                };
            });
        var completedAt = scenario == "lease" ? DateTime.UnixEpoch.AddMinutes(2) : DateTime.UnixEpoch;
        var retryAt = completedAt.AddMinutes(retryMinutes);
        SetupCompletion(
            claim,
            completedAt,
            isTerminal,
            retryAt,
            expectedFailure,
            cancellationToken);

        // Act
        var processed = await _dispatcher.DispatchAsync(
            _policy,
            cancellationToken);

        // Assert
        Assert.Equal(
            1,
            processed);
        Assert.True(providerToken.CanBeCanceled);
        Assert.NotEqual(
            cancellationToken,
            providerToken);
        Assert.Equal(
            scenario == "lease",
            providerToken.IsCancellationRequested);
        var log = Assert.Single(_logger.Entries);
        Assert.Contains(
            claim.EventId.ToString(),
            log);
        Assert.DoesNotContain(
            "PRIVATE",
            log);
        Assert.DoesNotContain(
            _message.RecipientAddress,
            log);
        Assert.DoesNotContain(
            _message.WishlistName,
            log);
        VerifyDelivery(
            claim,
            providerToken,
            cancellationToken);
        VerifyCompletion(
            claim,
            completedAt,
            isTerminal,
            retryAt,
            expectedFailure,
            cancellationToken);
        VerifyNoOtherCalls(5);
    }

    [Fact]
    public async Task DispatchAsync_WhenCallerCancelsDelivery_DoesNotAcknowledgeOrRetry()
    {
        // Arrange
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cancellationToken = cancellationSource.Token;
        var claim = CreateClaim(1);
        SetupDelivery(
            claim,
            cancellationToken);
        var providerToken = CancellationToken.None;
        _senderMock
            .Setup(sender => sender.SendAsync(
                _message,
                It.IsAny<CancellationToken>()))
            .Returns<WishlistModerationEmailMessage, CancellationToken>((
                _,
                token) =>
            {
                providerToken = token;
                cancellationSource.Cancel();

                return Task.FromCanceled(token);
            });

        // Act
        var action = () => _dispatcher.DispatchAsync(
            _policy,
            cancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.True(providerToken.IsCancellationRequested);
        Assert.Empty(_logger.Entries);
        VerifyDelivery(
            claim,
            providerToken,
            cancellationToken);
        VerifyNoOtherCalls(4);
    }

    private WishlistModerationEmailClaim CreateClaim(int attemptCount)
    {

        return new WishlistModerationEmailClaim
        {
            EventId = _message.EventId,
            LeaseId = Guid.CreateVersion7(),
            AttemptCount = attemptCount,
            LockedUntil = DateTime.UnixEpoch.AddMinutes(2)
        };
    }

    private void SetupClaim(
        WishlistModerationEmailClaim? claim,
        CancellationToken cancellationToken)
    {
        _repositoryMock
            .Setup(repository => repository.PurgeAsync(
                DateTime.UnixEpoch.AddDays(-30),
                1,
                cancellationToken))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(repository => repository.ClaimAsync(
                DateTime.UnixEpoch,
                _policy.LeaseDuration,
                cancellationToken))
            .ReturnsAsync(claim);
    }

    private void SetupDelivery(
        WishlistModerationEmailClaim claim,
        CancellationToken cancellationToken)
    {
        SetupClaim(
            claim,
            cancellationToken);
        _repositoryMock
            .Setup(repository => repository.GetMessageAsync(
                claim,
                DateTime.UnixEpoch,
                cancellationToken))
            .ReturnsAsync(_message);
        _clockMock
            .Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                TimeSpan.FromMinutes(2),
                Timeout.InfiniteTimeSpan))
            .Callback<TimerCallback, object?, TimeSpan, TimeSpan>((
                callback,
                state,
                _,
                _) => _expireLease = () => callback(state))
            .Returns(_timerMock.Object);
        _timerMock.Setup(timer => timer.Dispose());
    }

    private void SetupCompletion(
        WishlistModerationEmailClaim claim,
        DateTime completedAt,
        bool isTerminal,
        DateTime retryAt,
        string? failure,
        CancellationToken cancellationToken)
    {
        _repositoryMock
            .Setup(repository => repository.CompleteAsync(
                claim,
                completedAt,
                isTerminal,
                retryAt,
                failure,
                cancellationToken))
            .Returns(Task.CompletedTask);
    }

    private void VerifyClaim(CancellationToken cancellationToken)
    {
        _repositoryMock.Verify(
            repository => repository.PurgeAsync(
                DateTime.UnixEpoch.AddDays(-30),
                1,
                cancellationToken),
            Times.Once);
        _repositoryMock.Verify(
            repository => repository.ClaimAsync(
                DateTime.UnixEpoch,
                _policy.LeaseDuration,
                cancellationToken),
            Times.Once);
    }

    private void VerifyCompletion(
        WishlistModerationEmailClaim claim,
        DateTime completedAt,
        bool isTerminal,
        DateTime retryAt,
        string? failure,
        CancellationToken cancellationToken)
    {
        _repositoryMock.Verify(
            repository => repository.CompleteAsync(
                claim,
                completedAt,
                isTerminal,
                retryAt,
                failure,
                cancellationToken),
            Times.Once);
    }

    private void VerifyDelivery(
        WishlistModerationEmailClaim claim,
        CancellationToken providerToken,
        CancellationToken cancellationToken)
    {
        VerifyClaim(cancellationToken);
        _repositoryMock.Verify(
            repository => repository.GetMessageAsync(
                claim,
                DateTime.UnixEpoch,
                cancellationToken),
            Times.Once);
        _senderMock.Verify(
            sender => sender.SendAsync(
                _message,
                providerToken),
            Times.Once);
        _clockMock.Verify(
            clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                TimeSpan.FromMinutes(2),
                Timeout.InfiniteTimeSpan),
            Times.Once);
        _timerMock.Verify(
            timer => timer.Dispose(),
            Times.Once);
    }

    private void VerifyNoOtherCalls(int expectedClockReads)
    {
        _clockMock.Verify(
            clock => clock.GetUtcNow(),
            Times.Exactly(expectedClockReads));
        _repositoryMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
        _clockMock.VerifyNoOtherCalls();
        _timerMock.VerifyNoOtherCalls();
    }
}
