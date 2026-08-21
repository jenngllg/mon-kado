using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class AuthenticationEmailDispatcherTests
{
    private static readonly DateTime _now = new(
        2026,
        8,
        21,
        12,
        0,
        0,
        DateTimeKind.Utc);

    [Fact]
    public async Task DeliverMessageAsync_WhenClaimedMessageNoLongerExists_CompletesWithoutDelivery()
    {
        // Arrange
        var messageId = Guid.CreateVersion7();
        var outboxRepositoryMock = new Mock<IAuthenticationEmailOutboxRepository>(MockBehavior.Strict);
        outboxRepositoryMock
            .Setup(repository => repository.GetByIdForUpdateAsync(
                messageId,
                TestContext.Current.CancellationToken))
            .ReturnsAsync((AuthenticationEmailOutboxMessage?)null);
        await using var context = new MonKadoDbContext(
            new DbContextOptionsBuilder<MonKadoDbContext>()
                .UseNpgsql("Host=localhost;Database=mon_kado;Username=mon_kado;Password=test")
                .Options);
        var dispatcher = new AuthenticationEmailDispatcher(
            context,
            null!,
            null!,
            outboxRepositoryMock.Object,
            null!,
            null!,
            new FixedTimeProvider(new DateTimeOffset(_now)));

        // Act
        await dispatcher.DeliverMessageAsync(
            messageId,
            new Uri("https://mon-kado.fr"),
            TestContext.Current.CancellationToken);

        // Assert
        outboxRepositoryMock.Verify(repository => repository.GetByIdForUpdateAsync(
            messageId,
            TestContext.Current.CancellationToken), Times.Once);
        outboxRepositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void CanDeliver_WhenMessageHasAnActiveLease_ReturnsTrue()
    {
        // Arrange
        var message = CreateMessage();
        message.Claim(_now.AddMinutes(1));

        // Act
        var result = AuthenticationEmailDispatcher.CanDeliver(
            message,
            _now);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanDeliver_WhenMessageIsMissing_ReturnsFalse()
    {
        // Arrange
        // Act
        var result = AuthenticationEmailDispatcher.CanDeliver(
            null,
            _now);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CanDeliver_WhenLeaseIsNotActive_ReturnsFalse(int offsetMinutes)
    {
        // Arrange
        var message = CreateMessage();
        message.Claim(_now.AddMinutes(offsetMinutes));

        // Act
        var result = AuthenticationEmailDispatcher.CanDeliver(
            message,
            _now);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanDeliver_WhenMessageWasProcessed_ReturnsFalse()
    {
        // Arrange
        var message = CreateMessage();
        message.Claim(_now.AddMinutes(1));
        message.MarkProcessed(_now);

        // Act
        var result = AuthenticationEmailDispatcher.CanDeliver(
            message,
            _now);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanReceiveConfirmation_WhenUserIsEligible_ReturnsTrue()
    {
        // Arrange
        var user = CreateUser();

        // Act
        var result = AuthenticationEmailDispatcher.CanReceiveConfirmation(
            user,
            _now);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanReceiveConfirmation_WhenUserIsMissing_ReturnsFalse()
    {
        // Arrange
        // Act
        var result = AuthenticationEmailDispatcher.CanReceiveConfirmation(
            null,
            _now);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanReceiveConfirmation_WhenEmailIsMissing_ReturnsFalse()
    {
        // Arrange
        var user = CreateUser();
        user.Email = null;

        // Act
        var result = AuthenticationEmailDispatcher.CanReceiveConfirmation(
            user,
            _now);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanReceiveConfirmation_WhenAccountIsExpired_ReturnsFalse()
    {
        // Arrange
        var user = CreateUser();
        user.UnconfirmedAccountExpiresAt = _now;

        // Act
        var result = AuthenticationEmailDispatcher.CanReceiveConfirmation(
            user,
            _now);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanReceiveConfirmation_WhenEmailIsConfirmed_ReturnsFalse()
    {
        // Arrange
        var user = CreateUser();
        user.EmailConfirmed = true;

        // Act
        var result = AuthenticationEmailDispatcher.CanReceiveConfirmation(
            user,
            _now);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(1, AuthenticationEmailFailureCategory.Transient, 1)]
    [InlineData(2, AuthenticationEmailFailureCategory.Transient, 5)]
    [InlineData(3, AuthenticationEmailFailureCategory.Transient, 15)]
    [InlineData(4, AuthenticationEmailFailureCategory.Transient, 60)]
    [InlineData(5, AuthenticationEmailFailureCategory.Transient, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.RateLimited, 1)]
    [InlineData(1, AuthenticationEmailFailureCategory.Authentication, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.Permission, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.InvalidRequest, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.Unknown, 360)]
    public void GetRetryDelay_WhenProviderDelayIsAbsent_ReturnsConfiguredDelay(
        int attemptCount,
        AuthenticationEmailFailureCategory category,
        int expectedMinutes)
    {
        // Arrange

        // Act
        var delay = AuthenticationEmailDispatcher.GetRetryDelay(
            attemptCount,
            category,
            null);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            delay);
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(1_500, 1_440)]
    public void GetRetryDelay_WhenProviderDelayIsLonger_UsesCappedProviderDelay(
        int providerMinutes,
        int expectedMinutes)
    {
        // Arrange

        // Act
        var delay = AuthenticationEmailDispatcher.GetRetryDelay(
            1,
            AuthenticationEmailFailureCategory.Transient,
            TimeSpan.FromMinutes(providerMinutes));

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            delay);
    }

    [Fact]
    public void GetRetryDelay_WhenProviderDelayIsShorter_UsesConfiguredDelay()
    {
        // Arrange

        // Act
        var delay = AuthenticationEmailDispatcher.GetRetryDelay(
            3,
            AuthenticationEmailFailureCategory.Transient,
            TimeSpan.FromMinutes(1));

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            delay);
    }

    private static MonKadoUser CreateUser()
    {

        return new MonKadoUser
        {
            Id = Guid.NewGuid(),
            Email = "member@example.fr",
            EmailConfirmed = false,
            UnconfirmedAccountExpiresAt = _now.AddDays(1)
        };
    }

    private static AuthenticationEmailOutboxMessage CreateMessage()
    {

        return AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
            Guid.CreateVersion7(),
            _now);
    }
}
