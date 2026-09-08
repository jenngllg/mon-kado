using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Options;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class AccountErasureEmailDispatcherTests
{
    private readonly Mock<IAccountErasureEmailRepository> _repositoryMock;
    private readonly Mock<IAccountErasureRecipientProtector> _protectorMock;
    private readonly Mock<IAccountErasureEmailSender> _senderMock;
    private readonly Mock<TimeProvider> _clockMock;
    private readonly Mock<ITimer> _timerMock;
    private readonly AccountErasureEmailDispatcher _dispatcher;
    private readonly RecordingExceptionLogger<AccountErasureEmailDispatcher> _logger = new();
    private readonly AccountErasureProcessingOptions _options = new()
    {
        BatchSize = 1,
        RetryDelays =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5)
        ],
        MaximumRetryDelay = TimeSpan.FromHours(1)
    };

    public AccountErasureEmailDispatcherTests()
    {
        _repositoryMock = new Mock<IAccountErasureEmailRepository>(MockBehavior.Strict);
        _protectorMock = new Mock<IAccountErasureRecipientProtector>(MockBehavior.Strict);
        _senderMock = new Mock<IAccountErasureEmailSender>(MockBehavior.Strict);
        _clockMock = new Mock<TimeProvider>(MockBehavior.Strict);
        _timerMock = new Mock<ITimer>(MockBehavior.Strict);
        _clockMock.Setup(clock => clock.GetUtcNow())
            .Returns(DateTimeOffset.UnixEpoch);
        _dispatcher = new AccountErasureEmailDispatcher(
            _repositoryMock.Object,
            _protectorMock.Object,
            _senderMock.Object,
            Microsoft.Extensions.Options.Options.Create(_options),
            _clockMock.Object,
            _logger);
    }

    [Theory]
    [InlineData("empty", 0, 1)]
    [InlineData("expired", 1, 2)]
    public async Task DispatchAsync_WhenNoSendIsPermitted_DoesNotReadRecipient(
        string scenario,
        int expectedProcessed,
        int clockReads)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var claim = scenario == "empty" ? null : CreateClaim(
            1,
            DateTime.UnixEpoch);
        SetupClaim(
            claim,
            cancellationToken);

        // Act
        var processed = await _dispatcher.DispatchAsync(cancellationToken);

        // Assert
        Assert.Equal(
            expectedProcessed,
            processed);
        VerifyClaim(cancellationToken);
        VerifyNoOtherCalls(clockReads);
    }

    [Theory]
    [InlineData("success", 1, AccountErasureNotificationStatus.Accepted, 0)]
    [InlineData("unacknowledged", 1, AccountErasureNotificationStatus.Accepted, 0)]
    [InlineData("invalid", 1, AccountErasureNotificationStatus.Failed, 1)]
    [InlineData("unexpected", 1, AccountErasureNotificationStatus.Pending, 1)]
    [InlineData("timeout", 1, AccountErasureNotificationStatus.Pending, 1)]
    [InlineData("rate", 1, AccountErasureNotificationStatus.Pending, 3)]
    [InlineData("short-delay", 1, AccountErasureNotificationStatus.Pending, 1)]
    [InlineData("long-delay", 1, AccountErasureNotificationStatus.Pending, 60)]
    [InlineData("rejected", 3, AccountErasureNotificationStatus.Failed, 5)]
    [InlineData("rejected", 10, AccountErasureNotificationStatus.Failed, 5)]
    [InlineData("deadline", 1, AccountErasureNotificationStatus.Failed, 3)]
    public async Task DispatchAsync_WhenAttemptCompletes_PersistsFencedOutcomeAndSanitizedLogs(
        string scenario,
        int attemptCount,
        AccountErasureNotificationStatus expectedStatus,
        int retryMinutes)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var expiration = scenario == "deadline" ? DateTime.UnixEpoch.AddMinutes(1) : DateTime.UnixEpoch.AddHours(24);
        var claim = CreateClaim(
            attemptCount,
            expiration);
        SetupClaim(
            claim,
            cancellationToken);
        var timerDuration = scenario == "deadline" ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(2);
        SetupTimer(timerDuration);
        _protectorMock.Setup(protector => protector.Read(
                claim.OperationId,
                claim.ProtectedRecipient))
            .Returns(scenario == "invalid" ? null : "erased@example.test");
        var providerToken = CancellationToken.None;

        if (scenario != "invalid")
        {
            _senderMock.Setup(sender => sender.SendAsync(
                    claim.OperationId,
                    "erased@example.test",
                    claim.CreatedAt,
                    It.IsAny<CancellationToken>()))
                .Returns<Guid, string, DateTime, CancellationToken>((
                    _,
                    _,
                    _,
                    token) =>
                {
                    providerToken = token;

                    return scenario switch
                    {
                        "success" or "unacknowledged" => Task.CompletedTask,
                        "unexpected" => Task.FromException(new InvalidOperationException("private/path erased@example.test")),
                        "timeout" => Task.FromException(new OperationCanceledException("private/path")),
                        "short-delay" => Task.FromException(new AccountErasureEmailDeliveryException(
                            AccountErasureEmailFailure.RateLimited,
                            TimeSpan.FromSeconds(1))),
                        "long-delay" => Task.FromException(new AccountErasureEmailDeliveryException(
                            AccountErasureEmailFailure.RateLimited,
                            TimeSpan.MaxValue)),
                        "rejected" => Task.FromException(new AccountErasureEmailDeliveryException(
                            AccountErasureEmailFailure.Rejected,
                            null)),
                        _ => Task.FromException(new AccountErasureEmailDeliveryException(
                            AccountErasureEmailFailure.RateLimited,
                            TimeSpan.FromMinutes(3)))
                    };
                });
        }

        var retryAt = DateTime.UnixEpoch.AddMinutes(retryMinutes);
        _repositoryMock.Setup(repository => repository.CompleteAsync(
                claim,
                DateTime.UnixEpoch,
                expectedStatus,
                retryAt,
                cancellationToken))
            .ReturnsAsync(scenario != "unacknowledged");

        // Act
        var processed = await _dispatcher.DispatchAsync(cancellationToken);

        // Assert
        Assert.Equal(
            1,
            processed);
        VerifyClaim(cancellationToken);
        _protectorMock.Verify(protector => protector.Read(
                claim.OperationId,
                claim.ProtectedRecipient),
            Times.Once);

        if (scenario != "invalid")
        {
            Assert.True(providerToken.CanBeCanceled);
            _senderMock.Verify(sender => sender.SendAsync(
                    claim.OperationId,
                    "erased@example.test",
                    claim.CreatedAt,
                    providerToken),
                Times.Once);
        }

        _repositoryMock.Verify(repository => repository.CompleteAsync(
                claim,
                DateTime.UnixEpoch,
                expectedStatus,
                retryAt,
                cancellationToken),
            Times.Once);
        Assert.DoesNotContain(
            _logger.Entries,
            entry => entry.Contains("erased@example.test") || entry.Contains("private/path") || entry.Contains(claim.ProtectedRecipient));

        if (scenario == "unacknowledged")
            Assert.Empty(_logger.Entries);
        else
            Assert.Single(_logger.Entries);
        VerifyTimer(timerDuration);
        VerifyNoOtherCalls(3);
    }

    [Fact]
    public async Task DispatchAsync_WhenCallerCancelsDuringSend_PropagatesCancellationWithoutAcknowledging()
    {
        // Arrange
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cancellationToken = caller.Token;
        var claim = CreateClaim(
            1,
            DateTime.UnixEpoch.AddHours(24));
        SetupClaim(
            claim,
            cancellationToken);
        SetupTimer(TimeSpan.FromMinutes(2));
        _protectorMock.Setup(protector => protector.Read(
                claim.OperationId,
                claim.ProtectedRecipient))
            .Returns("erased@example.test");
        var providerToken = CancellationToken.None;
        _senderMock.Setup(sender => sender.SendAsync(
                claim.OperationId,
                "erased@example.test",
                claim.CreatedAt,
                It.IsAny<CancellationToken>()))
            .Returns<Guid, string, DateTime, CancellationToken>((
                _,
                _,
                _,
                token) =>
            {
                providerToken = token;
                caller.Cancel();

                return Task.FromCanceled(token);
            });

        // Act
        var failure = await Record.ExceptionAsync(() => _dispatcher.DispatchAsync(cancellationToken));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.True(providerToken.IsCancellationRequested);
        VerifyClaim(cancellationToken);
        _protectorMock.Verify(protector => protector.Read(
                claim.OperationId,
                claim.ProtectedRecipient),
            Times.Once);
        _senderMock.Verify(sender => sender.SendAsync(
                claim.OperationId,
                "erased@example.test",
                claim.CreatedAt,
                providerToken),
            Times.Once);
        VerifyTimer(TimeSpan.FromMinutes(2));
        Assert.Empty(_logger.Entries);
        VerifyNoOtherCalls(2);
    }

    private static AccountErasureEmailClaim CreateClaim(
        int attemptCount,
        DateTime expiresAt)
    {

        return TestFixture.Create()
            .Build<AccountErasureEmailClaim>()
            .With(
                claim => claim.AttemptCount,
                attemptCount)
            .With(
                claim => claim.LockedUntil,
                DateTime.UnixEpoch.AddMinutes(2))
            .With(
                claim => claim.ExpiresAt,
                expiresAt)
            .With(
                claim => claim.CreatedAt,
                DateTime.UnixEpoch)
            .Create();
    }

    private void SetupClaim(
        AccountErasureEmailClaim? claim,
        CancellationToken cancellationToken)
    {
        _repositoryMock.Setup(repository => repository.ClaimAsync(
                DateTime.UnixEpoch,
                _options.LeaseDuration,
                _options.MaximumAttempts,
                cancellationToken))
            .ReturnsAsync(claim);
    }

    private void SetupTimer(TimeSpan duration)
    {
        _clockMock.Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                duration,
                Timeout.InfiniteTimeSpan))
            .Returns(_timerMock.Object);
        _timerMock.Setup(timer => timer.Dispose());
    }

    private void VerifyClaim(CancellationToken cancellationToken)
    {
        _repositoryMock.Verify(repository => repository.ClaimAsync(
                DateTime.UnixEpoch,
                _options.LeaseDuration,
                _options.MaximumAttempts,
                cancellationToken),
            Times.Once);
    }

    private void VerifyTimer(TimeSpan duration)
    {
        _clockMock.Verify(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(),
                It.IsAny<object?>(),
                duration,
                Timeout.InfiniteTimeSpan),
            Times.Once);
        _timerMock.Verify(timer => timer.Dispose(), Times.Once);
    }

    private void VerifyNoOtherCalls(int expectedClockReads)
    {
        _clockMock.Verify(clock => clock.GetUtcNow(), Times.Exactly(expectedClockReads));
        _repositoryMock.VerifyNoOtherCalls();
        _protectorMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
        _clockMock.VerifyNoOtherCalls();
        _timerMock.VerifyNoOtherCalls();
    }
}
