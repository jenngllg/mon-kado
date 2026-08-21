using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class PostgreSqlAuthenticationTicketStoreTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        21,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly Guid _userId = Guid.Parse("0198d027-51c0-7000-8000-000000000001");
    private readonly Mock<IAuthenticationSessionRepository> _sessionRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly ServiceProvider _provider;
    private readonly PostgreSqlAuthenticationTicketStore _ticketStore;

    public PostgreSqlAuthenticationTicketStoreTests()
    {
        _sessionRepositoryMock = new(MockBehavior.Strict);
        _unitOfWorkMock = new(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddScoped(_ => _sessionRepositoryMock.Object);
        services.AddScoped(_ => _unitOfWorkMock.Object);
        _provider = services.BuildServiceProvider();
        _ticketStore = new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new EphemeralDataProtectionProvider(),
            new FixedTimeProvider(_now));
    }

    [Fact]
    public async Task StoreAsync_WhenTicketIsValid_PersistsSession()
    {
        // Arrange
        AuthenticationSession? persistedSession = null;
        var ticket = CreateTicket();
        _sessionRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<AuthenticationSession>()))
            .Callback<AuthenticationSession>(session => persistedSession = session);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);

        // Act
        var key = await _ticketStore.StoreAsync(
            ticket,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Guid.TryParseExact(
            key,
            "N",
            out var sessionId));
        Assert.NotNull(persistedSession);
        Assert.Equal(
            sessionId,
            persistedSession.Id);
        Assert.Equal(
            _userId,
            persistedSession.UserId);
        Assert.Equal(
            _now.UtcDateTime,
            persistedSession.CreatedAt);
        Assert.Equal(
            _now.AddHours(1).UtcDateTime,
            persistedSession.ExpiresAt);
        _sessionRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<AuthenticationSession>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StoreAsync_WhenUserIdentifierIsInvalid_ThrowsInvalidOperationException()
    {
        // Arrange
        var ticket = CreateTicket("invalid");

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _ticketStore.StoreAsync(
                ticket,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(
            "valid user identifier",
            exception.Message,
            StringComparison.Ordinal);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StoreAsync_WhenUserIdentifierIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var ticket = CreateTicket(Guid.Empty.ToString());

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _ticketStore.StoreAsync(
                ticket,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(
            "valid user identifier",
            exception.Message,
            StringComparison.Ordinal);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StoreAsync_WhenExpirationIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var ticket = CreateTicket(hasExpiration: false);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _ticketStore.StoreAsync(
                ticket,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(
            "require an expiration",
            exception.Message,
            StringComparison.Ordinal);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StoreAsync_WhenRepositoryTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var ticket = CreateTicket();
        _sessionRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<AuthenticationSession>()))
            .Throws(new TimeoutException());

        // Act
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            _ticketStore.StoreAsync(
                ticket,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "The PostgreSQL dependency is unavailable.",
            exception.Message);
        _sessionRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<AuthenticationSession>()),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StoreAsync_WhenCompatibilityOverloadsAreUsed_PersistsEachSession()
    {
        // Arrange
        var ticket = CreateTicket();
        _sessionRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<AuthenticationSession>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(CancellationToken.None))
            .ReturnsAsync(1);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);

        // Act
#pragma warning disable xUnit1051 // This test intentionally exercises the legacy overload without a token.
        var legacyKey = await _ticketStore.StoreAsync(ticket);
#pragma warning restore xUnit1051
        var contextualKey = await _ticketStore.StoreAsync(
            ticket,
            new DefaultHttpContext(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(
            legacyKey,
            contextualKey);
        _sessionRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<AuthenticationSession>()),
            Times.Exactly(2));
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(CancellationToken.None),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetrieveAsync_WhenKeyIsInvalid_ReturnsNull()
    {
        // Arrange

        // Act
        var ticket = await _ticketStore.RetrieveAsync(
            "invalid",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ticket);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("00000000000000000000000000000000")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task CompatibilityOverloads_WhenKeyIsEmpty_ReturnNoResults(string key)
    {
        // Arrange
        var ticket = CreateTicket();
        var context = new DefaultHttpContext();

        // Act
#pragma warning disable xUnit1051 // These calls intentionally exercise the legacy overloads without tokens.
        await _ticketStore.RenewAsync(
            key,
            ticket);
#pragma warning restore xUnit1051
        await _ticketStore.RenewAsync(
            key,
            ticket,
            context,
            TestContext.Current.CancellationToken);
#pragma warning disable xUnit1051 // This test intentionally exercises the legacy overload without a token.
        var legacyTicket = await _ticketStore.RetrieveAsync(key);
#pragma warning restore xUnit1051
        var contextualTicket = await _ticketStore.RetrieveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);
#pragma warning disable xUnit1051 // This test intentionally exercises the legacy overload without a token.
        await _ticketStore.RemoveAsync(key);
#pragma warning restore xUnit1051
        await _ticketStore.RemoveAsync(
            key,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(legacyTicket);
        Assert.Null(contextualTicket);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetrieveAsync_WhenSessionDoesNotExist_ReturnsNull()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7(_now);
        _sessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                sessionId,
                TestContext.Current.CancellationToken))
            .ReturnsAsync((AuthenticationSession?)null);

        // Act
        var ticket = await _ticketStore.RetrieveAsync(
            sessionId.ToString("N"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ticket);
        _sessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                sessionId,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetrieveAsync_WhenSessionIsExpired_DeletesSessionAndReturnsNull()
    {
        // Arrange
        var session = CreateSession(_now.AddMinutes(-1).UtcDateTime);
        _sessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                session.Id,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(session);
        _sessionRepositoryMock
            .Setup(repository => repository.DeleteAsync(
                session.Id,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var ticket = await _ticketStore.RetrieveAsync(
            session.Id.ToString("N"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ticket);
        _sessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                session.Id,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.Verify(
            repository => repository.DeleteAsync(
                session.Id,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetrieveAsync_WhenProtectedTicketIsCorrupted_DeletesSessionAndReturnsNull()
    {
        // Arrange
        var session = CreateSession(_now.AddHours(1).UtcDateTime);
        _sessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                session.Id,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(session);
        _sessionRepositoryMock
            .Setup(repository => repository.DeleteAsync(
                session.Id,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var ticket = await _ticketStore.RetrieveAsync(
            session.Id.ToString("N"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(ticket);
        _sessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                session.Id,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.Verify(
            repository => repository.DeleteAsync(
                session.Id,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetrieveAsync_WhenSessionIsValid_ReturnsOriginalTicket()
    {
        // Arrange
        AuthenticationSession? persistedSession = null;
        var originalTicket = CreateTicket();
        _sessionRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<AuthenticationSession>()))
            .Callback<AuthenticationSession>(session => persistedSession = session);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken))
            .ReturnsAsync(1);
        var key = await _ticketStore.StoreAsync(
            originalTicket,
            TestContext.Current.CancellationToken);
        var storedSession = persistedSession
            ?? throw new InvalidOperationException("The session was not captured.");
        _sessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                storedSession.Id,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(storedSession);

        // Act
        var retrievedTicket = await _ticketStore.RetrieveAsync(
            key,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(retrievedTicket);
        Assert.Equal(
            _userId.ToString(),
            retrievedTicket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(
            originalTicket.Properties.ExpiresUtc,
            retrievedTicket.Properties.ExpiresUtc);
        _sessionRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<AuthenticationSession>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                storedSession.Id,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RenewAsync_WhenKeyAndTicketAreValid_UpdatesSession()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7(_now);
        var ticket = CreateTicket();
        _sessionRepositoryMock
            .Setup(repository => repository.UpdateAsync(
                sessionId,
                _userId,
                It.IsAny<byte[]>(),
                _now.UtcDateTime,
                _now.AddHours(1).UtcDateTime,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _ticketStore.RenewAsync(
            sessionId.ToString("N"),
            ticket,
            TestContext.Current.CancellationToken);

        // Assert
        _sessionRepositoryMock.Verify(
            repository => repository.UpdateAsync(
                sessionId,
                _userId,
                It.IsAny<byte[]>(),
                _now.UtcDateTime,
                _now.AddHours(1).UtcDateTime,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RenewAsync_WhenExpirationIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7(_now);
        var ticket = CreateTicket(hasExpiration: false);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _ticketStore.RenewAsync(
                sessionId.ToString("N"),
                ticket,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(
            "require an expiration",
            exception.Message,
            StringComparison.Ordinal);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RenewAsync_WhenRepositoryTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7(_now);
        var ticket = CreateTicket();
        _sessionRepositoryMock
            .Setup(repository => repository.UpdateAsync(
                sessionId,
                _userId,
                It.IsAny<byte[]>(),
                _now.UtcDateTime,
                _now.AddHours(1).UtcDateTime,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            _ticketStore.RenewAsync(
                sessionId.ToString("N"),
                ticket,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "The PostgreSQL dependency is unavailable.",
            exception.Message);
        _sessionRepositoryMock.Verify(
            repository => repository.UpdateAsync(
                sessionId,
                _userId,
                It.IsAny<byte[]>(),
                _now.UtcDateTime,
                _now.AddHours(1).UtcDateTime,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveAsync_WhenKeyIsValid_DeletesSession()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7(_now);
        _sessionRepositoryMock
            .Setup(repository => repository.DeleteAsync(
                sessionId,
                TestContext.Current.CancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _ticketStore.RemoveAsync(
            sessionId.ToString("N"),
            TestContext.Current.CancellationToken);

        // Assert
        _sessionRepositoryMock.Verify(
            repository => repository.DeleteAsync(
                sessionId,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveAsync_WhenRepositoryTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7(_now);
        _sessionRepositoryMock
            .Setup(repository => repository.DeleteAsync(
                sessionId,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            _ticketStore.RemoveAsync(
                sessionId.ToString("N"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "The PostgreSQL dependency is unavailable.",
            exception.Message);
        _sessionRepositoryMock.Verify(
            repository => repository.DeleteAsync(
                sessionId,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RetrieveAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7(_now);
        _sessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                sessionId,
                TestContext.Current.CancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() =>
            _ticketStore.RetrieveAsync(
                sessionId.ToString("N"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "The PostgreSQL dependency is unavailable.",
            exception.Message);
        _sessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                sessionId,
                TestContext.Current.CancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        _provider.Dispose();
        GC.SuppressFinalize(this);
    }

    private static AuthenticationTicket CreateTicket(
        string? userId = null,
        bool hasExpiration = true)
    {
        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                userId ?? _userId.ToString())
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = hasExpiration
                ? _now.AddHours(1)
                : null
        };

        return new AuthenticationTicket(
            principal,
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static AuthenticationSession CreateSession(DateTime expiresAt)
    {
        return new()
        {
            Id = Guid.CreateVersion7(_now),
            UserId = _userId,
            ProtectedTicket = [
                1,
                2,
                3
            ],
            CreatedAt = _now.UtcDateTime,
            RenewedAt = _now.UtcDateTime,
            ExpiresAt = expiresAt
        };
    }
}
