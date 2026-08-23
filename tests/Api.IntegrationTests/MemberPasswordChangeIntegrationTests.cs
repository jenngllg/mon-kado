using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class MemberPasswordChangeIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string CurrentPassword = "a long current password";
    private const string NewPassword = "a long new password";
    private static readonly DateTimeOffset _referenceTime = DateTimeOffset.UtcNow;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdatePasswordAsync_WhenRequestIsValid_ChangesIdentityAndRevokesSecurityState(
        bool hasPendingEmailChange)
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(factory);
        var baseline = await GetMemberAsync(
            factory,
            member.Id);
        await CreateSessionsAsync(
            factory,
            member.Id);
        var requestId = hasPendingEmailChange
            ? await CreatePendingEmailChangeAsync(
                factory,
                member.Id)
            : (Guid?)null;
        timeProvider.Advance(TimeSpan.FromHours(1));
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);

        // Act
        using var response = await UpdatePasswordAsync(
            client,
            CurrentPassword,
            NewPassword);
        using var currentSessionResponse = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == member.Id,
                TestContext.Current.CancellationToken);
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .Where(session => session.UserId == member.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var messages = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Where(message => message.UserId == member.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var passwordNotification = Assert.Single(
            messages,
            message => message.Kind == AuthenticationEmailKind.PasswordChangedSecurityNotification);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        Assert.Equal(
            HttpStatusCode.OK,
            currentSessionResponse.StatusCode);
        Assert.NotEqual(
            baseline.PasswordHash,
            storedMember.PasswordHash);
        Assert.NotEqual(
            baseline.SecurityStamp,
            storedMember.SecurityStamp);
        Assert.NotEqual(
            baseline.Version,
            storedMember.Version);
        AssertTimestampClose(
            timeProvider.GetUtcNow().UtcDateTime,
            storedMember.UpdatedAt ?? DateTime.MinValue,
            TimeSpan.FromMilliseconds(1));
        Assert.False(await userManager.CheckPasswordAsync(
            storedMember,
            CurrentPassword));
        Assert.True(await userManager.CheckPasswordAsync(
            storedMember,
            NewPassword));
        Assert.NotEmpty(sessions);
        Assert.All(
            sessions,
            session => AssertTimestampClose(
                timeProvider.GetUtcNow().UtcDateTime,
                session.RevokedAt ?? DateTime.MinValue,
                TimeSpan.FromMilliseconds(1)));
        Assert.Equal(
            member.Email,
            passwordNotification.RecipientEmail);
        Assert.Null(passwordNotification.MemberEmailChangeRequestId);
        AssertTimestampClose(
            timeProvider.GetUtcNow().UtcDateTime,
            passwordNotification.CreatedAt,
            TimeSpan.FromMilliseconds(1));
        Assert.Null(passwordNotification.ProcessedAt);

        if (requestId is not { } existingRequestId)
            return;

        var emailChangeRequest = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(
                request => request.Id == existingRequestId,
                TestContext.Current.CancellationToken);
        AssertTimestampClose(
            timeProvider.GetUtcNow().UtcDateTime,
            emailChangeRequest.RevokedAt ?? DateTime.MinValue,
            TimeSpan.FromMilliseconds(1));
        Assert.All(
            messages.Where(message => message.MemberEmailChangeRequestId == existingRequestId),
            message => AssertTimestampClose(
                timeProvider.GetUtcNow().UtcDateTime,
                message.ProcessedAt ?? DateTime.MinValue,
                TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenRequestSucceeds_InvalidatesOldPublicAuthenticationFlows()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var emailConfirmation = await CreatePendingEmailChangeConfirmationAsync(
            factory,
            member.Id);
        using var firstLoginClient = factory.CreateClient();
        using var secondLoginClient = factory.CreateClient();
        using var firstLoginResponse = await LoginAsync(
            firstLoginClient,
            member.Email ?? string.Empty,
            CurrentPassword);
        using var secondLoginResponse = await LoginAsync(
            secondLoginClient,
            member.Email ?? string.Empty,
            CurrentPassword);
        firstLoginResponse.EnsureSuccessStatusCode();
        secondLoginResponse.EnsureSuccessStatusCode();
        var firstAccessToken = await ReadAccessTokenAsync(firstLoginResponse);
        var firstRefreshCookie = GetRefreshCookiePair(firstLoginResponse);
        var secondRefreshCookie = GetRefreshCookiePair(secondLoginResponse);
        using var passwordClient = factory.CreateClient();
        passwordClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            firstAccessToken.AccessToken);
        using var firstRefreshClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        using var secondRefreshClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        var firstCsrfExchange = await GetCsrfExchangeAsync(firstRefreshClient);
        var secondCsrfExchange = await GetCsrfExchangeAsync(secondRefreshClient);
        using var oldPasswordClient = factory.CreateClient();
        using var newPasswordClient = factory.CreateClient();
        using var emailConfirmationClient = factory.CreateClient();

        // Act
        using var passwordResponse = await UpdatePasswordAsync(
            passwordClient,
            CurrentPassword,
            NewPassword);
        using var firstRefreshResponse = await RefreshAsync(
            firstRefreshClient,
            firstCsrfExchange.Token,
            firstCsrfExchange.Cookie,
            firstRefreshCookie);
        using var secondRefreshResponse = await RefreshAsync(
            secondRefreshClient,
            secondCsrfExchange.Token,
            secondCsrfExchange.Cookie,
            secondRefreshCookie);
        using var oldPasswordResponse = await LoginAsync(
            oldPasswordClient,
            member.Email ?? string.Empty,
            CurrentPassword);
        using var newPasswordResponse = await LoginAsync(
            newPasswordClient,
            member.Email ?? string.Empty,
            NewPassword);
        using var emailConfirmationResponse = await ConfirmEmailChangeAsync(
            emailConfirmationClient,
            emailConfirmation.RequestId,
            emailConfirmation.Token);
        var emailConfirmationError =
            await emailConfirmationResponse.Content.ReadFromJsonAsync<ErrorResponse>(
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            passwordResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            firstRefreshResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            secondRefreshResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldPasswordResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            newPasswordResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            emailConfirmationResponse.StatusCode);
        Assert.NotNull(emailConfirmationError);
        Assert.Equal(
            ErrorCodes.MemberEmailChangeInvalid,
            emailConfirmationError.ErrorCode);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenCurrentPasswordIsInvalid_PreservesIdentityAndSessions()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var baseline = await GetMemberAsync(
            factory,
            member.Id);
        await CreateSessionsAsync(
            factory,
            member.Id);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);

        // Act
        using var response = await UpdatePasswordAsync(
            client,
            "the wrong current password",
            NewPassword);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == member.Id,
                TestContext.Current.CancellationToken);
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .Where(session => session.UserId == member.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
        Assert.Equal(
            baseline.PasswordHash,
            storedMember.PasswordHash);
        Assert.Equal(
            baseline.SecurityStamp,
            storedMember.SecurityStamp);
        Assert.All(
            sessions,
            session => Assert.Null(session.RevokedAt));
        Assert.False(await context.AuthenticationEmailOutboxMessages.AnyAsync(
            message => message.Kind == AuthenticationEmailKind.PasswordChangedSecurityNotification,
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(
        "password mismatch failure",
        HttpStatusCode.Forbidden,
        ErrorCodes.MemberCurrentPasswordInvalid,
        null)]
    [InlineData(
        "password short failure",
        HttpStatusCode.BadRequest,
        ErrorCodes.RequestValidationError,
        ValidationMessages.PasswordTooShort)]
    [InlineData(
        "password long failure",
        HttpStatusCode.BadRequest,
        ErrorCodes.RequestValidationError,
        ValidationMessages.PasswordTooLong)]
    [InlineData(
        "password digit failure",
        HttpStatusCode.BadRequest,
        ErrorCodes.RequestValidationError,
        "The password requires a digit.")]
    [InlineData(
        "password unique failure",
        HttpStatusCode.BadRequest,
        ErrorCodes.RequestValidationError,
        "The password requires more unique characters.")]
    [InlineData(
        "password symbol failure",
        HttpStatusCode.BadRequest,
        ErrorCodes.RequestValidationError,
        "The password requires a non-alphanumeric character.")]
    [InlineData(
        "password lower failure",
        HttpStatusCode.BadRequest,
        ErrorCodes.RequestValidationError,
        "The password requires a lowercase letter.")]
    [InlineData(
        "password upper failure",
        HttpStatusCode.BadRequest,
        ErrorCodes.RequestValidationError,
        "The password requires an uppercase letter.")]
    [InlineData(
        "password unexpected failure",
        HttpStatusCode.InternalServerError,
        null,
        null)]
    [InlineData(
        "password timeout failure",
        HttpStatusCode.ServiceUnavailable,
        ErrorCodes.TechnicalDependencyUnavailable,
        null)]
    public async Task UpdatePasswordAsync_WhenIdentityRejectsChange_ReturnsExpectedErrorAndPreservesState(
        string newPassword,
        HttpStatusCode expectedStatusCode,
        string? expectedErrorCode,
        string? expectedValidationMessage)
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            configureServices: services =>
            {
                services.RemoveAll<UserManager<MonKadoUser>>();
                services.AddScoped<UserManager<MonKadoUser>, FailingMemberPasswordUserManager>();
            });
        var member = await CreateMemberAsync(factory);
        var baseline = await GetMemberAsync(
            factory,
            member.Id);
        await CreateSessionsAsync(
            factory,
            member.Id);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);

        // Act
        using var response = await UpdatePasswordAsync(
            client,
            CurrentPassword,
            newPassword);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == member.Id,
                TestContext.Current.CancellationToken);
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .Where(session => session.UserId == member.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            expectedErrorCode,
            error.ErrorCode);
        Assert.Equal(
            baseline.PasswordHash,
            storedMember.PasswordHash);
        Assert.Equal(
            baseline.SecurityStamp,
            storedMember.SecurityStamp);
        Assert.All(
            sessions,
            session => Assert.Null(session.RevokedAt));
        Assert.False(await context.AuthenticationEmailOutboxMessages.AnyAsync(
            message => message.Kind == AuthenticationEmailKind.PasswordChangedSecurityNotification,
            TestContext.Current.CancellationToken));

        if (expectedValidationMessage is null)
            return;

        var validationError = Assert.Single(error.ValidationErrors ?? []);
        Assert.Equal(
            "newPassword",
            validationError.PropertyName);
        Assert.Equal(
            expectedValidationMessage,
            validationError.ErrorMessage);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenOutboxPersistenceFails_RollsBackEntireSecurityChange()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var baseline = await GetMemberAsync(
            factory,
            member.Id);
        await CreateSessionsAsync(
            factory,
            member.Id);
        var requestId = await CreatePendingEmailChangeAsync(
            factory,
            member.Id);
        await AddPasswordNotificationRejectionConstraintAsync(factory);

        // Act
        HttpResponseMessage response;
        try
        {
            using var client = CreateAuthorizedClient(
                factory,
                member.Id);
            response = await UpdatePasswordAsync(
                client,
                CurrentPassword,
                NewPassword);
        }
        finally
        {
            await RemovePasswordNotificationRejectionConstraintAsync(factory);
        }

        using (response)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var storedMember = await context.Users
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.Id == member.Id,
                    TestContext.Current.CancellationToken);
            var sessions = await context.AuthenticationSessions
                .AsNoTracking()
                .Where(session => session.UserId == member.Id)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            var emailChangeRequest = await context.MemberEmailChangeRequests
                .AsNoTracking()
                .SingleAsync(
                    request => request.Id == requestId,
                    TestContext.Current.CancellationToken);
            var messages = await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .Where(message => message.UserId == member.Id)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();

            // Assert
            Assert.Equal(
                HttpStatusCode.InternalServerError,
                response.StatusCode);
            Assert.Equal(
                baseline.PasswordHash,
                storedMember.PasswordHash);
            Assert.Equal(
                baseline.SecurityStamp,
                storedMember.SecurityStamp);
            Assert.Equal(
                baseline.Version,
                storedMember.Version);
            Assert.True(await userManager.CheckPasswordAsync(
                storedMember,
                CurrentPassword));
            Assert.False(await userManager.CheckPasswordAsync(
                storedMember,
                NewPassword));
            Assert.All(
                sessions,
                session => Assert.Null(session.RevokedAt));
            Assert.Null(emailChangeRequest.RevokedAt);
            Assert.All(
                messages,
                message => Assert.Null(message.ProcessedAt));
            Assert.DoesNotContain(
                messages,
                message => message.Kind ==
                    AuthenticationEmailKind.PasswordChangedSecurityNotification);
        }
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenMemberDoesNotExist_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await UpdatePasswordAsync(
            client,
            CurrentPassword,
            NewPassword);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenChangedTwice_AllowsMultiplePendingNotifications()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);

        // Act
        using var firstResponse = await UpdatePasswordAsync(
            client,
            CurrentPassword,
            NewPassword);
        using var secondResponse = await UpdatePasswordAsync(
            client,
            NewPassword,
            "a third secure password");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var notificationCount = await context.AuthenticationEmailOutboxMessages.CountAsync(
            message => message.Kind == AuthenticationEmailKind.PasswordChangedSecurityNotification,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            secondResponse.StatusCode);
        Assert.Equal(
            2,
            notificationCount);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenTwoChangesAreConcurrent_SerializesPasswordVerification()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var firstClient = CreateAuthorizedClient(
            factory,
            member.Id);
        using var secondClient = CreateAuthorizedClient(
            factory,
            member.Id);

        // Act
        var responses = await Task.WhenAll(
            UpdatePasswordAsync(
                firstClient,
                CurrentPassword,
                "first concurrent password"),
            UpdatePasswordAsync(
                secondClient,
                CurrentPassword,
                "second concurrent password"));

        // Assert
        Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.Forbidden);

        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenLoginIsConcurrent_RevokesSessionCreatedWithOldPassword()
    {
        // Arrange
        var coordinator = new FirstSaveChangesCoordinator();
        await using var factory = await CreateMigratedFactoryAsync(
            configureServices: services => ConfigureCoordinatedUnitOfWork(
                services,
                coordinator));
        var member = await CreateMemberAsync(factory);
        using var loginClient = factory.CreateClient();
        using var passwordClient = CreateAuthorizedClient(
            factory,
            member.Id);
        using var refreshClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        var csrfExchange = await GetCsrfExchangeAsync(refreshClient);

        // Act
        var loginTask = LoginAsync(
            loginClient,
            member.Email ?? string.Empty,
            CurrentPassword);
        await coordinator.WaitUntilFirstSaveStartsAsync(
            TestContext.Current.CancellationToken);
        var memberLocked = await IsMemberLockedAsync(
            factory,
            member.Id);
        var passwordTask = UpdatePasswordAsync(
            passwordClient,
            CurrentPassword,
            NewPassword);
        coordinator.ReleaseFirstSave();
        using var loginResponse = await loginTask;
        using var passwordResponse = await passwordTask;
        var refreshCookie = GetRefreshCookiePair(loginResponse);
        using var refreshResponse = await RefreshAsync(
            refreshClient,
            csrfExchange.Token,
            csrfExchange.Cookie,
            refreshCookie);

        // Assert
        Assert.True(memberLocked);
        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            passwordResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            refreshResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatePasswordAsync_WhenRefreshIsConcurrent_CompletesWithoutLockInversion()
    {
        // Arrange
        var coordinator = new SessionLockCoordinator();
        await using var factory = await CreateMigratedFactoryAsync(
            configureServices: services => ConfigureCoordinatedSessionRepository(
                services,
                coordinator));
        var member = await CreateMemberAsync(factory);
        using var loginClient = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            loginClient,
            member.Email ?? string.Empty,
            CurrentPassword);
        loginResponse.EnsureSuccessStatusCode();
        var accessToken = await ReadAccessTokenAsync(loginResponse);
        var refreshCookie = GetRefreshCookiePair(loginResponse);
        using var passwordClient = factory.CreateClient();
        passwordClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.AccessToken);
        using var refreshClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        var csrfExchange = await GetCsrfExchangeAsync(refreshClient);

        // Act
        var refreshTask = RefreshAsync(
            refreshClient,
            csrfExchange.Token,
            csrfExchange.Cookie,
            refreshCookie);
        await coordinator.WaitUntilSessionIsLockedAsync(
            TestContext.Current.CancellationToken);
        var memberLocked = await IsMemberLockedAsync(
            factory,
            member.Id);
        var passwordTask = UpdatePasswordAsync(
            passwordClient,
            CurrentPassword,
            NewPassword);
        coordinator.ReleaseSession();
        using var refreshResponse = await refreshTask;
        using var passwordResponse = await passwordTask;
        var rotatedRefreshCookie = GetRefreshCookiePair(refreshResponse);
        using var revokedRefreshResponse = await RefreshAsync(
            refreshClient,
            csrfExchange.Token,
            csrfExchange.Cookie,
            rotatedRefreshCookie);

        // Assert
        Assert.True(memberLocked);
        Assert.Equal(
            HttpStatusCode.OK,
            refreshResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            passwordResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            revokedRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task RefreshAsync_WhenSessionIsDeletedAfterLookup_ReturnsUnauthorized()
    {
        // Arrange
        var coordinator = new SessionLockCoordinator(
            coordinateLookup: true,
            coordinateLock: false);
        await using var factory = await CreateMigratedFactoryAsync(
            configureServices: services => ConfigureCoordinatedSessionRepository(
                services,
                coordinator));
        var member = await CreateMemberAsync(factory);
        using var loginClient = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            loginClient,
            member.Email ?? string.Empty,
            CurrentPassword);
        loginResponse.EnsureSuccessStatusCode();
        var refreshCookie = GetRefreshCookiePair(loginResponse);
        using var refreshClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        var csrfExchange = await GetCsrfExchangeAsync(refreshClient);

        // Act
        var refreshTask = RefreshAsync(
            refreshClient,
            csrfExchange.Token,
            csrfExchange.Cookie,
            refreshCookie);
        await coordinator.WaitUntilLookupCompletesAsync(
            TestContext.Current.CancellationToken);
        await DeleteSessionsAsync(
            factory,
            member.Id);
        coordinator.ReleaseLookup();
        using var refreshResponse = await refreshTask;

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            refreshResponse.StatusCode);
    }

    [Fact]
    public async Task ChangeAsync_WhenCancellationIsRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemberPasswordService>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var action = () => service.ChangeAsync(
            Guid.CreateVersion7(),
            CurrentPassword,
            NewPassword,
            cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync(
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider ?? new FixedTimeProvider(_referenceTime),
            configureServices: configureServices);
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    private static void ConfigureCoordinatedUnitOfWork(
        IServiceCollection services,
        FirstSaveChangesCoordinator coordinator)
    {
        services.RemoveAll<IUnitOfWork>();
        services.AddSingleton(coordinator);
        services.AddScoped<IUnitOfWork, CoordinatedUnitOfWork>();
    }

    private static void ConfigureCoordinatedSessionRepository(
        IServiceCollection services,
        SessionLockCoordinator coordinator)
    {
        services.RemoveAll<IAuthenticationSessionRepository>();
        services.AddSingleton(coordinator);
        services.AddScoped<AuthenticationSessionRepository>();
        services.AddScoped<IAuthenticationSessionRepository, CoordinatedAuthenticationSessionRepository>();
    }

    private static async Task<MonKadoUser> CreateMemberAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_referenceTime),
            Email = "member@example.fr",
            UserName = "member@example.fr",
            DisplayName = "Password change test",
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(
            member,
            CurrentPassword);
        Assert.True(
            result.Succeeded,
            string.Join(
                ", ",
                result.Errors.Select(error => error.Description)));

        return member;
    }

    private static async Task<MonKadoUser> GetMemberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Users
            .AsNoTracking()
            .SingleAsync(
                member => member.Id == memberId,
                TestContext.Current.CancellationToken);
    }

    private static async Task CreateSessionsAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var now = _referenceTime.UtcDateTime;
        context.AuthenticationSessions.AddRange(
            AuthenticationSession.Create(
                Guid.CreateVersion7(_referenceTime),
                memberId,
                new byte[32],
                isPersistent: false,
                now,
                now.AddHours(8)),
            AuthenticationSession.Create(
                Guid.CreateVersion7(_referenceTime.AddMilliseconds(1)),
                memberId,
                Enumerable.Repeat(
                    (byte)1,
                    32).ToArray(),
                isPersistent: true,
                now,
                now.AddDays(30)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task DeleteSessionsAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.AuthenticationSessions
            .Where(session => session.UserId == memberId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> IsMemberLockedAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken);
        try
        {
            _ = await context.Users
                .FromSqlInterpolated(
                    $"SELECT *, xmin FROM public.users WHERE id = {memberId} FOR UPDATE NOWAIT")
                .SingleAsync(TestContext.Current.CancellationToken);

            return false;
        }
        catch (PostgresException exception) when (
            exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {

            return true;
        }
        catch (InvalidOperationException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.LockNotAvailable
            })
        {

            return true;
        }
    }

    private static async Task<Guid> CreatePendingEmailChangeAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var now = _referenceTime.UtcDateTime;
        var request = MemberEmailChangeRequest.Create(
            memberId,
            "member@example.fr",
            "new-member@example.fr",
            "NEW-MEMBER@EXAMPLE.FR",
            now,
            now.AddHours(24));
        context.MemberEmailChangeRequests.Add(request);
        context.AuthenticationEmailOutboxMessages.AddRange(
            AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
                request.Id,
                memberId,
                request.NewEmail,
                now),
            AuthenticationEmailOutboxMessage.CreateEmailChangeSecurityNotification(
                request.Id,
                memberId,
                request.CurrentEmail,
                now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return request.Id;
    }

    private static async Task<(Guid RequestId, string Token)>
        CreatePendingEmailChangeConfirmationAsync(
            PostgreSqlApiFactory factory,
            Guid memberId)
    {
        var requestId = await CreatePendingEmailChangeAsync(
            factory,
            memberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == requestId,
                TestContext.Current.CancellationToken);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await userManager.FindByIdAsync(memberId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        var purpose = MemberEmailChangeTokenPurpose.Create(
            request.Id,
            request.NormalizedNewEmail);
        var token = await userManager.GenerateUserTokenAsync(
            member,
            EmailChangeTokenProviderOptions.ProviderName,
            purpose);

        return (
            request.Id,
            AuthenticationEmailTokenEncoding.Encode(token));
    }

    private static async Task AddPasswordNotificationRejectionConstraintAsync(
        PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE public.authentication_email_outbox " +
            "ADD CONSTRAINT ck_test_reject_password_notification " +
            "CHECK (kind <> 'PASSWORD_CHANGED_SECURITY_NOTIFICATION');",
            TestContext.Current.CancellationToken);
    }

    private static async Task RemovePasswordNotificationRejectionConstraintAsync(
        PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE public.authentication_email_outbox " +
            "DROP CONSTRAINT IF EXISTS ck_test_reject_password_notification;",
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                rememberMe = false
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(
        HttpClient client,
        string csrfToken,
        string antiforgeryCookie,
        string refreshCookie)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions/refresh");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);
        request.Headers.Add(
            "Cookie",
            $"{antiforgeryCookie}; {refreshCookie}");

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> ConfirmEmailChangeAsync(
        HttpClient client,
        Guid requestId,
        string token)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/email-change-confirmations")
        {
            Content = JsonContent.Create(new
            {
                requestId,
                token
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

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

    private static async Task<(string Token, string Cookie)> GetCsrfExchangeAsync(
        HttpClient client)
    {
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

        return (
            payload.Token,
            cookie);
    }

    private static async Task<AccessTokenResponse> ReadAccessTokenAsync(
        HttpResponseMessage response)
    {

        return await response.Content.ReadFromJsonAsync<AccessTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The access token response is empty.");
    }

    private static string GetRefreshCookiePair(HttpResponseMessage response)
    {

        return response.Headers.GetValues("Set-Cookie")
            .Select(GetCookiePair)
            .Single(value => value.StartsWith(
                "MonKado.Refresh=",
                StringComparison.Ordinal));
    }

    private static string GetCookiePair(string value)
    {

        return value.Split(';')[0];
    }

    private static HttpClient CreateAuthorizedClient(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static async Task<HttpResponseMessage> UpdatePasswordAsync(
        HttpClient client,
        string currentPassword,
        string newPassword)
    {

        return await client.PutAsJsonAsync(
            "/api/v1/members/current/password",
            new
            {
                currentPassword,
                newPassword
            },
            TestContext.Current.CancellationToken);
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
