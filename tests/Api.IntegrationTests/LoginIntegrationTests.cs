using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class LoginIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "  a long secure password  ";
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task LoginAsync_WhenAccountDoesNotExist_ReturnsUnauthorizedWithoutSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "missing@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountInvalidCredentials,
            await GetErrorCodeAsync(response));
        Assert.False(response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies) && cookies.Any(value => value.StartsWith(
                "MonKado.Refresh=",
            StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LoginAsync_WhenAccountDisappearsBeforeDatabaseLock_ReturnsInvalidCredentials()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.RemoveAll<IMonKadoUserRepository>();
                services.AddScoped<IMonKadoUserRepository, MissingLockedUserRepository>();
            });
        await CreateUserAsync(
            factory,
            "missing-lock@example.fr",
            emailConfirmed: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        var result = await service.LoginAsync(
            "missing-lock@example.fr",
            Password,
            rememberMe: false,
            currentRefreshToken: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            result.Result);
        Assert.Null(result.Tokens);
    }

    [Theory]
    [InlineData(false, "record the failed login attempt")]
    [InlineData(true, "reset the failed login count")]
    public async Task LoginAsync_WhenIdentityCannotPersistFailureState_ThrowsDetailedException(
        bool usesValidPassword,
        string expectedOperation)
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.RemoveAll<UserManager<MonKadoUser>>();
                services.AddScoped<UserManager<MonKadoUser>, FailingIdentityUpdateUserManager>();
            });
        var user = await CreateUserAsync(
            factory,
            "identity-failure@example.fr",
            emailConfirmed: true);

        if (usesValidPassword)
        {
            await using var setupScope = factory.Services.CreateAsyncScope();
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.Users
                .Where(value => value.Id == user.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        value => value.AccessFailedCount,
                        1),
                    TestContext.Current.CancellationToken);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        Task<AccountSessionLoginResult> action() => service.LoginAsync(
            "identity-failure@example.fr",
            usesValidPassword ? Password : "wrong password",
            rememberMe: false,
            currentRefreshToken: null,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            (Func<Task<AccountSessionLoginResult>>)action);
        Assert.Contains(
            expectedOperation,
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "PersistenceFailed",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginAsync_WhenAccessTokenCreationFails_DoesNotPersistSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.RemoveAll<IAccessTokenService>();
                services.AddSingleton<IAccessTokenService, ThrowingAccessTokenService>();
            });
        await CreateUserAsync(
            factory,
            "token-failure@example.fr",
            emailConfirmed: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        Task<AccountSessionLoginResult> action() => service.LoginAsync(
            "token-failure@example.fr",
            Password,
            rememberMe: false,
            currentRefreshToken: null,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            (Func<Task<AccountSessionLoginResult>>)action);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoginAsync_WhenCommitAcknowledgementIsLost_ReturnsOriginalRefreshSecretOnce()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        var user = await CreateUserAsync(
            factory,
            "ambiguous-login@example.fr",
            emailConfirmed: true);
        interceptor.Arm();

        // Act
        var result = await LoginInNewScopeAsync(
            factory,
            "ambiguous-login@example.fr",
            Password);

        // Assert
        Assert.Equal(
            AccountLoginResult.Success,
            result.Result);
        Assert.NotNull(result.Tokens);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            user.Id,
            session.UserId);
        Assert.Null(session.RevokedAt);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.Tokens.RefreshToken)),
            session.RefreshTokenHash);
    }

    [Fact]
    public async Task LoginAsync_WhenInvalidPasswordCommitAcknowledgementIsLost_IncrementsFailureOnlyOnce()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "ambiguous-failure@example.fr",
            emailConfirmed: true);
        interceptor.Arm();

        // Act
        var result = await LoginInNewScopeAsync(
            factory,
            "ambiguous-failure@example.fr",
            "wrong password");

        // Assert
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            result.Result);
        Assert.Null(result.Tokens);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            1,
            user.AccessFailedCount);
        Assert.Null(user.LockoutEnd);
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoginAsync_WhenConcurrentInvalidPasswordCommitsBeforeAmbiguousResult_DoesNotReplayFirstFailure()
    {
        // Arrange
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "concurrent-failure@example.fr",
            emailConfirmed: true);
        interceptor.Arm();
        var ambiguousAttempt = LoginInNewScopeAsync(
            factory,
            "concurrent-failure@example.fr",
            "first wrong password");
        await interceptor.WaitForFirstCommitAsync(
            TestContext.Current.CancellationToken);

        // Act
        var concurrentResult = await LoginInNewScopeAsync(
            factory,
            "concurrent-failure@example.fr",
            "second wrong password");
        interceptor.ReleaseFailure();
        var ambiguousResult = await ambiguousAttempt;

        // Assert
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            concurrentResult.Result);
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            ambiguousResult.Result);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            user.AccessFailedCount);
        Assert.Null(user.LockoutEnd);
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoginAsync_WhenCommittedSessionIsRevokedBeforeVerification_CreatesUsableReplacementSession()
    {
        // Arrange
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "revoked-ambiguous-login@example.fr",
            emailConfirmed: true);
        interceptor.Arm();
        var ambiguousAttempt = LoginInNewScopeAsync(
            factory,
            "revoked-ambiguous-login@example.fr",
            Password);
        await interceptor.WaitForFirstCommitAsync(
            TestContext.Current.CancellationToken);

        await using (var revocationScope = factory.Services.CreateAsyncScope())
        {
            var context = revocationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var session = await context.AuthenticationSessions
                .SingleAsync(TestContext.Current.CancellationToken);
            session.Revoke(_now.UtcDateTime);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        interceptor.ReleaseFailure();
        var result = await ambiguousAttempt;

        // Assert
        Assert.Equal(
            AccountLoginResult.Success,
            result.Result);
        Assert.NotNull(result.Tokens);
        await using var scope = factory.Services.CreateAsyncScope();
        var verificationContext = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sessions = await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .OrderBy(session => session.CreatedAt)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            sessions.Length);
        Assert.Single(
            sessions,
            session => session.RevokedAt is not null);
        var activeSession = Assert.Single(
            sessions,
            session => session.RevokedAt is null);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.Tokens.RefreshToken)),
            activeSession.RefreshTokenHash);
    }

    [Fact]
    public async Task LoginAsync_WhenCommittedSessionIsDeletedBeforeVerification_CreatesUsableSession()
    {
        // Arrange
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "deleted-ambiguous-login@example.fr",
            emailConfirmed: true);
        interceptor.Arm();
        var ambiguousAttempt = LoginInNewScopeAsync(
            factory,
            "deleted-ambiguous-login@example.fr",
            Password);
        await interceptor.WaitForFirstCommitAsync(
            TestContext.Current.CancellationToken);

        await using (var deletionScope = factory.Services.CreateAsyncScope())
        {
            var context = deletionScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.AuthenticationSessions
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        // Act
        interceptor.ReleaseFailure();
        var result = await ambiguousAttempt;

        // Assert
        Assert.Equal(
            AccountLoginResult.Success,
            result.Result);
        Assert.NotNull(result.Tokens);
        await using var scope = factory.Services.CreateAsyncScope();
        var verificationContext = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(session.RevokedAt);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.Tokens.RefreshToken)),
            session.RefreshTokenHash);
    }

    [Fact]
    public async Task LoginAsync_WhenCommittedSessionHashChangesBeforeVerification_CreatesUsableReplacementSession()
    {
        // Arrange
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "changed-ambiguous-login@example.fr",
            emailConfirmed: true);
        interceptor.Arm();
        var ambiguousAttempt = LoginInNewScopeAsync(
            factory,
            "changed-ambiguous-login@example.fr",
            Password);
        await interceptor.WaitForFirstCommitAsync(
            TestContext.Current.CancellationToken);

        await using (var rotationScope = factory.Services.CreateAsyncScope())
        {
            var context = rotationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var session = await context.AuthenticationSessions
                .SingleAsync(TestContext.Current.CancellationToken);
            var concurrentToken = new RefreshTokenService().Create(session.Id);
            session.Rotate(
                concurrentToken.Hash,
                _now.UtcDateTime,
                session.ExpiresAt);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        interceptor.ReleaseFailure();
        var result = await ambiguousAttempt;

        // Assert
        Assert.Equal(
            AccountLoginResult.Success,
            result.Result);
        Assert.NotNull(result.Tokens);
        await using var scope = factory.Services.CreateAsyncScope();
        var verificationContext = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sessions = await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            sessions.Length);
        Assert.Single(
            sessions,
            session => SHA256.HashData(Encoding.UTF8.GetBytes(result.Tokens.RefreshToken))
                .SequenceEqual(session.RefreshTokenHash));
    }

    [Fact]
    public async Task RefreshAsync_WhenAccessTokenCreationFails_PreservesCurrentSessionToken()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.RemoveAll<IAccessTokenService>();
                services.AddSingleton<IAccessTokenService, ThrowingAccessTokenService>();
            });
        var user = await CreateUserAsync(
            factory,
            "refresh-token-failure@example.fr",
            emailConfirmed: true);
        var sessionId = Guid.CreateVersion7(_now.UtcDateTime);
        var refreshToken = new RefreshTokenService().Create(sessionId);
        var expiresAt = _now.UtcDateTime.AddHours(8);

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            setupContext.AuthenticationSessions.Add(AuthenticationSession.Create(
                sessionId,
                user.Id,
                refreshToken.Hash,
                isPersistent: false,
                _now.UtcDateTime,
                expiresAt));
            await setupContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        Task<AccountSessionTokens?> action() => service.RefreshAsync(
            refreshToken.Value,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            (Func<Task<AccountSessionTokens?>>)action);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            refreshToken.Hash,
            session.RefreshTokenHash);
        AssertTimestampClose(
            _now.UtcDateTime,
            session.RenewedAt,
            TimeSpan.FromMilliseconds(1));
        AssertTimestampClose(
            expiresAt,
            session.ExpiresAt,
            TimeSpan.FromMilliseconds(1));
        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenCommitAcknowledgementIsLost_ReturnsOriginalRefreshSecret()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "ambiguous-refresh@example.fr",
            emailConfirmed: true);
        var loginResult = await LoginInNewScopeAsync(
            factory,
            "ambiguous-refresh@example.fr",
            Password);
        Assert.NotNull(loginResult.Tokens);
        interceptor.Arm();

        // Act
        var result = await RefreshInNewScopeAsync(
            factory,
            loginResult.Tokens.RefreshToken);

        // Assert
        Assert.NotNull(result);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(session.RevokedAt);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.RefreshToken)),
            session.RefreshTokenHash);
    }

    [Fact]
    public async Task RefreshAsync_WhenCommittedRotationIsConcurrentlyRevoked_ReturnsInvalidSession()
    {
        // Arrange
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "concurrent-refresh@example.fr",
            emailConfirmed: true);
        var loginResult = await LoginInNewScopeAsync(
            factory,
            "concurrent-refresh@example.fr",
            Password);
        Assert.NotNull(loginResult.Tokens);
        var originalRefreshToken = loginResult.Tokens.RefreshToken;
        interceptor.Arm();
        var ambiguousAttempt = RefreshInNewScopeAsync(
            factory,
            originalRefreshToken);
        await interceptor.WaitForFirstCommitAsync(
            TestContext.Current.CancellationToken);

        // Act
        var concurrentResult = await RefreshInNewScopeAsync(
            factory,
            originalRefreshToken);
        interceptor.ReleaseFailure();
        var ambiguousResult = await ambiguousAttempt;

        // Assert
        Assert.Null(concurrentResult);
        Assert.Null(ambiguousResult);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenRolledBackAttemptObservesConcurrentWinner_DoesNotReturnLosingRefreshSecret()
    {
        // Arrange
        var interceptor = new ConcurrentWinnerCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "winner-refresh@example.fr",
            emailConfirmed: true);
        var loginResult = await LoginInNewScopeAsync(
            factory,
            "winner-refresh@example.fr",
            Password);
        Assert.NotNull(loginResult.Tokens);
        var originalRefreshToken = loginResult.Tokens.RefreshToken;
        interceptor.Arm();
        var losingAttempt = RefreshInNewScopeAsync(
            factory,
            originalRefreshToken);
        await interceptor.WaitForFirstCommitAttemptAsync(
            TestContext.Current.CancellationToken);

        // Act
        var winnerResult = await RefreshInNewScopeAsync(
            factory,
            originalRefreshToken);
        interceptor.ReleaseVerification();
        var losingResult = await losingAttempt;

        // Assert
        Assert.NotNull(winnerResult);
        Assert.Null(losingResult);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(winnerResult.RefreshToken)),
            session.RefreshTokenHash);
    }

    [Fact]
    public async Task RefreshAsync_WhenRevocationCommitAcknowledgementIsLost_ReturnsInvalidSession()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "ambiguous-revocation@example.fr",
            emailConfirmed: true);
        var loginResult = await LoginInNewScopeAsync(
            factory,
            "ambiguous-revocation@example.fr",
            Password);
        Assert.NotNull(loginResult.Tokens);
        var sessionId = new RefreshTokenService()
            .TryGetSessionId(
                loginResult.Tokens.RefreshToken,
                out var parsedSessionId)
            ? parsedSessionId
            : throw new InvalidOperationException("The login refresh token must be valid.");
        var alteredToken = $"{sessionId:N}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        interceptor.Arm();

        // Act
        var result = await RefreshInNewScopeAsync(
            factory,
            alteredToken);

        // Assert
        Assert.Null(result);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenRevokedSessionIsDeletedBeforeAmbiguousVerification_ReturnsInvalidSession()
    {
        // Arrange
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        await CreateUserAsync(
            factory,
            "deleted-revocation@example.fr",
            emailConfirmed: true);
        var loginResult = await LoginInNewScopeAsync(
            factory,
            "deleted-revocation@example.fr",
            Password);
        Assert.NotNull(loginResult.Tokens);
        var sessionId = new RefreshTokenService()
            .TryGetSessionId(
                loginResult.Tokens.RefreshToken,
                out var parsedSessionId)
            ? parsedSessionId
            : throw new InvalidOperationException("The login refresh token must be valid.");
        var alteredToken = $"{sessionId:N}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        interceptor.Arm();
        var ambiguousAttempt = RefreshInNewScopeAsync(
            factory,
            alteredToken);
        await interceptor.WaitForFirstCommitAsync(
            TestContext.Current.CancellationToken);

        await using (var deletionScope = factory.Services.CreateAsyncScope())
        {
            var context = deletionScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.AuthenticationSessions
                .Where(session => session.Id == sessionId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        // Act
        interceptor.ReleaseFailure();
        var result = await ambiguousAttempt;

        // Assert
        Assert.Null(result);
        await using var scope = factory.Services.CreateAsyncScope();
        var verificationContext = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshAsync_WhenUnknownSessionCommitAcknowledgementIsLost_ReturnsInvalidSession()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_now),
            services =>
            {
                services.AddSingleton(interceptor);
                services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor));
            });
        var refreshToken = new RefreshTokenService()
            .Create(Guid.CreateVersion7(_now.UtcDateTime));
        interceptor.Arm();

        // Act
        var result = await RefreshInNewScopeAsync(
            factory,
            refreshToken.Value);

        // Assert
        Assert.Null(result);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("invalid", false)]
    [InlineData("unknown", false)]
    [InlineData("altered", false)]
    [InlineData("expired", false)]
    [InlineData("revoked", true)]
    public async Task LoginAsync_WhenCurrentBrowserTokenCannotBeProven_PreservesPriorSessionState(
        string scenario,
        bool wasPreviouslyRevoked)
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "replacement@example.fr",
            emailConfirmed: true);
        string firstRefreshToken;

        await using (var loginScope = factory.Services.CreateAsyncScope())
        {
            var service = loginScope.ServiceProvider.GetRequiredService<IAccountSessionService>();
            var firstLogin = await service.LoginAsync(
                "replacement@example.fr",
                Password,
                rememberMe: false,
                currentRefreshToken: null,
                TestContext.Current.CancellationToken);
            Assert.NotNull(firstLogin.Tokens);
            firstRefreshToken = firstLogin.Tokens.RefreshToken;
        }

        Guid firstSessionId;

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var session = await setupContext.AuthenticationSessions.SingleAsync(
                TestContext.Current.CancellationToken);
            firstSessionId = session.Id;

            if (scenario == "expired")
                timeProvider.UtcNow = _now.AddHours(9);

            if (scenario == "revoked")
            {
                session.Revoke(_now.UtcDateTime);
                await setupContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
        }

        var currentRefreshToken = scenario switch
        {
            "invalid" => "invalid",
            "unknown" => new RefreshTokenService().Create(Guid.CreateVersion7()).Value,
            "altered" => new RefreshTokenService().Create(firstSessionId).Value,
            "expired" => firstRefreshToken,
            "revoked" => firstRefreshToken,
            _ => throw new InvalidOperationException($"Unknown test scenario '{scenario}'.")
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var accountSessionService = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        var result = await accountSessionService.LoginAsync(
            "replacement@example.fr",
            Password,
            rememberMe: false,
            currentRefreshToken,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            AccountLoginResult.Success,
            result.Result);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            sessions.Length);
        var revokedAt = sessions.Single(value => value.Id == firstSessionId).RevokedAt;

        if (!wasPreviouslyRevoked)
        {
            Assert.Null(revokedAt);

            return;
        }

        Assert.NotNull(revokedAt);
        AssertTimestampClose(
            _now.UtcDateTime,
            revokedAt.Value,
            TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task LoginAsync_WhenConfirmedAccount_ReturnsMinimalJwtAndStoresRefreshHash()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            " LEA@example.fr ",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var payload = await ReadAccessTokenAsync(response);
        Assert.Equal(
            "Bearer",
            payload.TokenType);
        Assert.Equal(
            900,
            payload.ExpiresIn);
        AssertMinimalToken(
            payload.AccessToken,
            user.Id);

        var refreshCookie = GetRefreshCookie(response);
        Assert.Contains(
            "; path=/",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "; httponly",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "; samesite=strict",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "; expires=",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);

        var refreshToken = GetCookieValue(refreshCookie);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            user.Id,
            session.UserId);
        Assert.False(session.IsPersistent);
        Assert.Null(session.RevokedAt);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)),
            session.RefreshTokenHash);
        Assert.DoesNotContain(
            refreshToken,
            Convert.ToHexString(session.RefreshTokenHash),
            StringComparison.Ordinal);
        AssertTimestampClose(
            _now.UtcDateTime.AddHours(8),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RefreshAsync_WhenSessionIsValid_RotatesTokenAndSlidesExpiration()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var firstToken = GetCookieValue(GetRefreshCookie(loginResponse));
        timeProvider.UtcNow = _now.AddMinutes(10);

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var secondToken = GetCookieValue(GetRefreshCookie(response));
        Assert.NotEqual(
            firstToken,
            secondToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(secondToken)),
            session.RefreshTokenHash);
        AssertTimestampClose(
            timeProvider.UtcNow.UtcDateTime,
            session.RenewedAt,
            TimeSpan.FromSeconds(1));
        AssertTimestampClose(
            timeProvider.UtcNow.UtcDateTime.AddHours(8),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RefreshAsync_WhenSameTokenIsRotatedConcurrently_AllowsOneAndRevokesSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var refreshToken = GetCookieValue(GetRefreshCookie(loginResponse));
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRotation = RefreshWhenReleasedAsync(
            firstService,
            refreshToken,
            gate.Task);
        var secondRotation = RefreshWhenReleasedAsync(
            secondService,
            refreshToken,
            gate.Task);

        // Act
        gate.SetResult(true);
        var results = await Task.WhenAll(
            firstRotation,
            secondRotation);

        // Assert
        Assert.Single(
            results,
            result => result is not null);
        Assert.Single(
            results,
            result => result is null);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(session.RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenPreviousTokenIsReused_RevokesSessionAndDeletesCookie()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var previousCookie = GetRefreshCookie(loginResponse);
        using var rotatedResponse = await RefreshAsync(client);
        Assert.Equal(
            HttpStatusCode.OK,
            rotatedResponse.StatusCode);
        var csrf = await GetCsrfExchangeAsync(factory);

        // Act
        using var response = await RefreshAsync(
            csrf.Client,
            csrf.Token,
            string.Join(
                "; ",
                csrf.Cookie,
                GetCookiePair(previousCookie)));

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            await GetErrorCodeAsync(response));
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
        csrf.Client.Dispose();
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenIsAltered_RevokesSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var refreshToken = GetCookieValue(GetRefreshCookie(loginResponse));
        var sessionId = refreshToken.Split('.')[0];
        var alteredToken = $"{sessionId}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var csrf = await GetCsrfExchangeAsync(factory);

        // Act
        using var response = await RefreshAsync(
            csrf.Client,
            csrf.Token,
            string.Join(
                "; ",
                csrf.Cookie,
                $"MonKado.Refresh={alteredToken}"));

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
        csrf.Client.Dispose();
    }

    [Fact]
    public async Task RefreshAsync_WhenSessionIsExpired_ReturnsUnauthorizedAndRevokesSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        timeProvider.UtcNow = _now.AddHours(8).AddSeconds(1);

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenMemberWasDeleted_ReturnsUnauthorizedWithoutSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Users
                .Where(value => value.Id == user.Id)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCurrentAsync_WhenMemberIsAuthenticated_ReturnsPersistedIdentityAndRoles()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var user = await CreateUserAsync(
            factory,
            "current@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "current@example.fr",
            Password,
            rememberMe: false);
        var accessToken = await ReadAccessTokenAsync(loginResponse);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.AccessToken);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var currentSession = await response.Content.ReadFromJsonAsync<CurrentSessionResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(currentSession);
        Assert.Equal(
            user.Id,
            currentSession.Id);
        Assert.Equal(
            user.Email,
            currentSession.Email);
        Assert.Equal(
            user.DisplayName,
            currentSession.DisplayName);
        Assert.Equal(
            [RoleNames.Member],
            currentSession.Roles);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenRolesChange_ReturnsUpdatedRolesWithSameAccessToken()
    {
        // Arrange
        const string AdministratorRole = "Administrator";
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var user = await CreateUserAsync(
            factory,
            "roles@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "roles@example.fr",
            Password,
            rememberMe: false);
        var accessToken = await ReadAccessTokenAsync(loginResponse);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.AccessToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>
            {
                Id = Guid.CreateVersion7(),
                Name = AdministratorRole
            });
            Assert.True(roleResult.Succeeded);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
            var persistedUser = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persistedUser);
            var roleAssignmentResult = await userManager.AddToRoleAsync(
                persistedUser,
                AdministratorRole);
            Assert.True(roleAssignmentResult.Succeeded);
        }

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var currentSession = await response.Content.ReadFromJsonAsync<CurrentSessionResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(currentSession);
        Assert.Equal(
            [
                AdministratorRole,
                RoleNames.Member
            ],
            currentSession.Roles);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken.AccessToken);
        Assert.DoesNotContain(
            token.Claims,
            claim => claim.Type == System.Security.Claims.ClaimTypes.Role);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenMemberWasDeleted_ReturnsUnauthorizedAndDeletesRefreshCookie()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var user = await CreateUserAsync(
            factory,
            "deleted-current@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "deleted-current@example.fr",
            Password,
            rememberMe: false);
        var accessToken = await ReadAccessTokenAsync(loginResponse);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.AccessToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Users
                .Where(member => member.Id == user.Id)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            await GetErrorCodeAsync(response));
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenFormatIsInvalid_ReturnsNullBeforeDatabaseLookup()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        var result = await service.RefreshAsync(
            "invalid-refresh-token",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData(-1, ErrorCodes.AccountInvalidCredentials)]
    [InlineData(1, ErrorCodes.AccountEmailNotConfirmed)]
    [InlineData(null, ErrorCodes.AccountEmailNotConfirmed)]
    public async Task LoginAsync_WhenAccountIsUnconfirmed_ReturnsExpectedUnauthorized(
        int? expirationDays,
        string expectedErrorCode)
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: false);
        var expiresAt = expirationDays is null
            ? (DateTime?)null
            : _now.UtcDateTime.AddDays(Math.Max(
                1,
                expirationDays.Value));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Users
                .Where(value => value.Id == user.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        value => value.UnconfirmedAccountExpiresAt,
                        expiresAt),
                    TestContext.Current.CancellationToken);
        }

        if (expirationDays < 0)
            timeProvider.UtcNow = _now.AddDays(2);

        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            expectedErrorCode,
            await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task LoginAsync_WhenPreviousFailureExists_ResetsFailureCount()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
            var persistedUser = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persistedUser);
            Assert.True((await userManager.AccessFailedAsync(persistedUser)).Succeeded);
        }
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            0,
            (await context.Users
                .AsNoTracking()
                .SingleAsync(TestContext.Current.CancellationToken)).AccessFailedCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenMemberRowIsMissing_RevokesOrphanedSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SET session_replication_role = replica;",
                    TestContext.Current.CancellationToken);
                await context.Users
                    .Where(value => value.Id == user.Id)
                    .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SET session_replication_role = origin;",
                    TestContext.Current.CancellationToken);
                await context.Database.CloseConnectionAsync();
            }
        }

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenRememberMe_RotatesWithoutExtendingAbsoluteExpiration()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: true);
        Assert.Contains(
            "; expires=",
            GetRefreshCookie(loginResponse),
            StringComparison.OrdinalIgnoreCase);
        timeProvider.UtcNow = _now.AddDays(1);

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(session.IsPersistent);
        AssertTimestampClose(
            _now.UtcDateTime.AddDays(30),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LoginAsync_WhenSameBrowserSignsInAgain_RevokesOnlyPreviousSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var firstBrowser = factory.CreateClient();
        using var secondBrowser = factory.CreateClient();
        using var firstResponse = await LoginAsync(
            firstBrowser,
            "lea@example.fr",
            Password,
            rememberMe: false);
        using var otherBrowserResponse = await LoginAsync(
            secondBrowser,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Act
        using var replacementResponse = await LoginAsync(
            firstBrowser,
            "lea@example.fr",
            Password,
            rememberMe: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            replacementResponse.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .OrderBy(session => session.CreatedAt)
            .ThenBy(session => session.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            3,
            sessions.Length);
        Assert.Single(
            sessions,
            session => session.RevokedAt is not null);
        Assert.Equal(
            2,
            sessions.Count(session => session.RevokedAt is null));
    }

    [Fact]
    public async Task LoginAsync_WhenFiveInvalidPasswords_LocksAccountWithoutCreatingSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failedResponse = await LoginAsync(
                client,
                "lea@example.fr",
                "wrong password",
                rememberMe: false);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                failedResponse.StatusCode);
        }

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == user.Id,
                TestContext.Current.CancellationToken);
        Assert.NotNull(persistedUser.LockoutEnd);
        Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteExpiredSessionsAsync_WhenCutoffReached_DeletesOnlyExpiredSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "short@example.fr",
            emailConfirmed: true);
        await CreateUserAsync(
            factory,
            "persistent@example.fr",
            emailConfirmed: true);
        using var shortClient = factory.CreateClient();
        using var persistentClient = factory.CreateClient();
        using var shortResponse = await LoginAsync(
            shortClient,
            "short@example.fr",
            Password,
            rememberMe: false);
        using var persistentResponse = await LoginAsync(
            persistentClient,
            "persistent@example.fr",
            Password,
            rememberMe: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAuthenticationSessionCleanup>();

        // Act
        var deletedCount = await cleanup.DeleteExpiredSessionsAsync(
            _now.UtcDateTime.AddHours(8),
            500,
            TestContext.Current.CancellationToken);
        var secondDeletedCount = await cleanup.DeleteExpiredSessionsAsync(
            _now.UtcDateTime.AddHours(8),
            500,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            deletedCount);
        Assert.Equal(
            0,
            secondDeletedCount);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync(
        TimeProvider timeProvider,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider,
            configureServices: configureServices);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<MonKadoUser> CreateUserAsync(
        PostgreSqlApiFactory factory,
        string email,
        bool emailConfirmed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_now),
            Email = email,
            UserName = email,
            DisplayName = "Login test",
            EmailConfirmed = emailConfirmed,
            UnconfirmedAccountExpiresAt = emailConfirmed
                ? null
                : _now.UtcDateTime.AddDays(30)
        };
        var result = await userManager.CreateAsync(
            user,
            Password);
        Assert.True(
            result.Succeeded,
            string.Join(
                ", ",
                result.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(
            user,
            RoleNames.Member);
        Assert.True(
            roleResult.Succeeded,
            string.Join(
                ", ",
                roleResult.Errors.Select(error => error.Description)));

        return user;
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password,
        bool rememberMe)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                rememberMe
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions/refresh");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<AccountSessionTokens?> RefreshWhenReleasedAsync(
        IAccountSessionService service,
        string refreshToken,
        Task gate)
    {
        await gate;

        return await service.RefreshAsync(
            refreshToken,
            TestContext.Current.CancellationToken);
    }

    private static async Task<AccountSessionLoginResult> LoginInNewScopeAsync(
        PostgreSqlApiFactory factory,
        string email,
        string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        return await service.LoginAsync(
            email,
            password,
            rememberMe: false,
            currentRefreshToken: null,
            TestContext.Current.CancellationToken);
    }

    private static async Task<AccountSessionTokens?> RefreshInNewScopeAsync(
        PostgreSqlApiFactory factory,
        string refreshToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        return await service.RefreshAsync(
            refreshToken,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(
        HttpClient client,
        string csrfToken,
        string cookies)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions/refresh");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);
        request.Headers.Add(
            "Cookie",
            cookies);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");

        return payload.Token;
    }

    private static async Task<CsrfExchange> GetCsrfExchangeAsync(PostgreSqlApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        using var response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(GetCookiePair)
            .Single(value => value.StartsWith(
                "MonKado.Antiforgery=",
                StringComparison.Ordinal));

        return new CsrfExchange(
            client,
            payload.Token,
            cookie);
    }

    private static async Task<AccessTokenResponse> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<AccessTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The access token response is empty.");
    }

    private static string GetRefreshCookie(HttpResponseMessage response)
    {
        return response.Headers.GetValues("Set-Cookie").Single(header =>
            header.StartsWith(
                "MonKado.Refresh=",
                StringComparison.Ordinal) &&
            !header.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    private static string GetCookiePair(string value)
    {
        return value.Split(';')[0];
    }

    private static string GetCookieValue(string value)
    {
        return GetCookiePair(value).Split(
            '=',
            2)[1];
    }

    private static async Task<string?> GetErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication error response is empty.");

        return document.RootElement.GetProperty("errorCode").GetString();
    }

    private static void AssertMinimalToken(
        string value,
        Guid expectedUserId)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(value);
        var claimTypes = token.Claims
            .Select(claim => claim.Type)
            .OrderBy(type => type)
            .ToArray();
        var expectedClaimTypes = new[]
        {
            JwtRegisteredClaimNames.Aud,
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.Iat,
            JwtRegisteredClaimNames.Iss,
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Sub
        }
        .OrderBy(type => type)
        .ToArray();
        Assert.Equal(
            expectedClaimTypes,
            claimTypes);
        Assert.Equal(
            expectedUserId.ToString("D"),
            token.Subject);
    }

    private static void AssertTimestampClose(
        DateTime expected,
        DateTime actual,
        TimeSpan tolerance)
    {
        Assert.InRange(
            actual,
            expected.Subtract(tolerance),
            expected.Add(tolerance));
    }

}
