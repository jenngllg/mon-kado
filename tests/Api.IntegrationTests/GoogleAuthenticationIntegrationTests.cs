using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class GoogleAuthenticationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "a long secure password";
    private static readonly DateTimeOffset _now = new(
        2030,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task CompleteAsync_WhenNewGmailIdentity_CreatesPasswordlessMemberRoleLoginAndHashedSession()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var logCapture = new CapturingGoogleLoggerProvider();
        await using var factory = CreateFactory(services =>
            services.AddSingleton<ILoggerProvider>(logCapture));
        var flowId = Guid.CreateVersion7(_now);
        var subject = new string(
            'S',
            255);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                subject,
                "new-member@gmail.com"),
            flowId,
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        Assert.NotNull(result.Session);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            7,
            user.Id.Version);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        AssertSecurityStamp(user.SecurityStamp);
        Assert.Equal(
            "Google member",
            user.DisplayName);
        Assert.Null(user.UnconfirmedAccountExpiresAt);
        var login = await context.UserLogins
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            ExternalLoginProviders.Google,
            login.LoginProvider);
        Assert.Equal(
            subject,
            login.ProviderKey);
        Assert.Equal(
            user.Id,
            login.UserId);
        var role = await context.UserRoles
            .Where(userRole => userRole.UserId == user.Id)
            .Join(
                context.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (
                    _,
                    role) => role.Name)
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            RoleNames.Member,
            role);
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            flowId,
            session.Id);
        Assert.Equal(
            user.Id,
            session.UserId);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.Session.RefreshToken)),
            session.RefreshTokenHash);
        Assert.DoesNotContain(
            result.Session.RefreshToken,
            Convert.ToHexString(session.RefreshTokenHash),
            StringComparison.Ordinal);
        var googleEntries = logCapture.Entries
            .Where(entry => entry.Key is >= LogEventIds.GoogleMemberCreated and <=
                LogEventIds.GoogleSessionCreated)
            .ToArray();
        Assert.Equal(
            [
                LogEventIds.GoogleMemberCreated,
                LogEventIds.GoogleSessionCreated
            ],
            googleEntries.Select(entry => entry.Key));
        Assert.All(
            googleEntries,
            entry =>
            {
                Assert.DoesNotContain(
                    subject,
                    entry.Value,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "new-member@gmail.com",
                    entry.Value,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "Google member",
                    entry.Value,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    result.Session.RefreshToken,
                    entry.Value,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    flowId.ToString("D"),
                    entry.Value,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 720)]
    public async Task CompleteAsync_WhenGoogleSessionPersistenceIsSelected_StoresExpectedLifetime(
        bool isPersistent,
        int expectedLifetimeHours)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "lifetime-subject",
                "lifetime@gmail.com"),
            Guid.CreateVersion7(_now),
            null,
            isPersistent);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Session);
        Assert.Equal(
            isPersistent,
            result.Session.IsPersistent);
        Assert.Equal(
            _now.UtcDateTime.AddHours(expectedLifetimeHours),
            result.Session.RefreshTokenExpiresAt);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            isPersistent,
            session.IsPersistent);
        Assert.Equal(
            _now.UtcDateTime.AddHours(expectedLifetimeHours),
            session.ExpiresAt);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("confirmed")]
    [InlineData("unconfirmed")]
    public async Task CompleteAsync_WhenThirdPartyEmailStateVaries_ReturnsSameAdditionalVerificationOutcome(
        string emailState)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var existingMember = emailState == "free"
            ? null
            : await CreateLocalMemberAsync(
                factory,
                "member@example.com",
                emailConfirmed: emailState == "confirmed",
                TestContext.Current.CancellationToken);

        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            existingMember?.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.AdditionalVerificationRequired,
            result.Outcome);
        Assert.Null(result.Session);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        var storedMembers = await context.Users
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);

        if (existingMember is null)
        {
            Assert.Empty(storedMembers);

            return;
        }

        var storedMember = Assert.Single(storedMembers);
        Assert.Equal(
            existingMember.Id,
            storedMember.Id);
        Assert.Equal(
            emailState == "confirmed",
            storedMember.EmailConfirmed);
    }

    [Fact]
    public async Task CompleteAsync_WhenUnconfirmedGmailAccountIsReclaimed_InvalidatesLocalCredentialAndSecurityState()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var user = await CreateLocalMemberAsync(
            factory,
            "member@gmail.com",
            emailConfirmed: false,
            TestContext.Current.CancellationToken);
        var baselineSecurityStamp = user.SecurityStamp;
        _ = await CreateCurrentSessionAsync(
            factory,
            user.Id,
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var accountSessionService = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        var googleAccountSessionService =
            scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
        var loginBeforeTakeover = await accountSessionService.LoginAsync(
            "member@gmail.com",
            Password,
            false,
            null,
            TestContext.Current.CancellationToken);
        await SetPasswordFailureStateAsync(
            factory,
            user.Id,
            accessFailedCount: 4,
            _now.AddMinutes(15),
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "gmail-subject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now),
            user.Id);

        // Act
        var result = await googleAccountSessionService.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);
        var loginAfterTakeover = await accountSessionService.LoginAsync(
            "member@gmail.com",
            Password,
            false,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            AccountLoginResult.EmailNotConfirmed,
            loginBeforeTakeover.Result);
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            loginAfterTakeover.Result);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            user.Id,
            storedUser.Id);
        Assert.True(storedUser.EmailConfirmed);
        Assert.Null(storedUser.PasswordHash);
        Assert.NotEqual(
            baselineSecurityStamp,
            storedUser.SecurityStamp);
        AssertSecurityStamp(storedUser.SecurityStamp);
        Assert.Equal(
            0,
            storedUser.AccessFailedCount);
        Assert.Null(storedUser.LockoutEnd);
        Assert.Null(storedUser.UnconfirmedAccountExpiresAt);
        Assert.Equal(
            "Google member",
            storedUser.DisplayName);
        Assert.Equal(
            user.Id,
            await context.UserLogins
                .AsNoTracking()
                .Select(login => login.UserId)
                .SingleAsync(TestContext.Current.CancellationToken));
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            sessions.Length);
        Assert.Single(
            sessions,
            session => session.Id == authenticationContext.FlowId &&
                session.RevokedAt is null);
        Assert.Single(
            sessions,
            session => session.Id != authenticationContext.FlowId &&
                session.RevokedAt == _now.UtcDateTime);
    }

    [Fact]
    public async Task LoginAsync_WhenGoogleOnlyMemberReceivesPasswordAttempts_DoesNotMutateLockoutAndGoogleStillAuthenticates()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var passwordHasher = new CapturingPasswordHasher();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IPasswordHasher<MonKadoUser>>();
            services.AddSingleton<IPasswordHasher<MonKadoUser>>(passwordHasher);
        });
        var initialContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "passwordless-workspace-subject",
                "passwordless@company.example",
                true,
                "company.example",
                null),
            Guid.CreateVersion7(_now),
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var googleService = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
        var accountSessionService = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        var initialResult = await googleService.CompleteAsync(
            initialContext,
            TestContext.Current.CancellationToken);

        // Act
        var passwordResults = (List<AccountSessionLoginResult>)[];

        for (var attempt = 0; attempt < 5; attempt++)
            passwordResults.Add(await accountSessionService.LoginAsync(
                "passwordless@company.example",
                "unknown password",
                false,
                null,
                TestContext.Current.CancellationToken));

        var reconnectResult = await googleService.CompleteAsync(
            CreateAuthenticationContext(
                initialContext.Identity,
                Guid.CreateVersion7(_now.AddMinutes(1)),
                initialResult.MemberId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            passwordResults,
            result => Assert.Equal(
                AccountLoginResult.InvalidCredentials,
                result.Result));
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            reconnectResult.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(storedMember.PasswordHash);
        Assert.Equal(
            0,
            storedMember.AccessFailedCount);
        Assert.Null(storedMember.LockoutEnd);
        Assert.Equal(
            5,
            passwordHasher.HashCount);
        Assert.Equal(
            2,
            await context.AuthenticationSessions.CountAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenGoogleOnlyWorkspaceMemberReceivesPasswordAttempts_DoesNotMutateLockoutOrOriginalLogin()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var passwordHasher = new CapturingPasswordHasher();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IPasswordHasher<MonKadoUser>>();
            services.AddSingleton<IPasswordHasher<MonKadoUser>>(passwordHasher);
        });
        var originalIdentity = new GoogleIdentity(
            "original-workspace-subject",
            "workspace@company.example",
            true,
            "company.example",
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
        var initialResult = await service.CompleteAsync(
            CreateAuthenticationContext(
                originalIdentity,
                Guid.CreateVersion7(_now),
                null),
            TestContext.Current.CancellationToken);
        var linkContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "reassigned-workspace-subject",
                "workspace@company.example",
                true,
                "company.example",
                null),
            Guid.CreateVersion7(_now.AddMinutes(1)),
            initialResult.MemberId);

        // Act
        var linkResults = (List<GoogleAccountLinkResult>)[];

        for (var attempt = 0; attempt < 5; attempt++)
            linkResults.Add(await service.LinkAsync(
                linkContext,
                "unknown password",
                TestContext.Current.CancellationToken));

        var reconnectResult = await service.CompleteAsync(
            CreateAuthenticationContext(
                originalIdentity,
                Guid.CreateVersion7(_now.AddMinutes(2)),
                initialResult.MemberId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            linkResults,
            result => Assert.Equal(
                GoogleAccountLinkOutcome.InvalidCredentials,
                result.Outcome));
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            reconnectResult.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            0,
            storedMember.AccessFailedCount);
        Assert.Null(storedMember.LockoutEnd);
        Assert.Equal(
            5,
            passwordHasher.HashCount);
        Assert.Equal(
            "original-workspace-subject",
            await context.UserLogins
                .AsNoTracking()
                .Select(login => login.ProviderKey)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            2,
            await context.AuthenticationSessions.CountAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenExistingWorkspaceEmailAndNewSubject_RequiresPasswordWithoutAutoLink()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var user = await CreateLocalMemberAsync(
            factory,
            "member@company.example",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "reassigned-workspace-subject",
                "member@company.example",
                true,
                "company.example",
                "Different employee"),
            Guid.CreateVersion7(_now),
            user.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.ExplicitLinkRequired,
            result.Outcome);
        Assert.Null(result.Session);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            user.Id,
            await context.Users
                .AsNoTracking()
                .Select(member => member.Id)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenWorkspaceMemberIsUnconfirmed_ClaimsAccountWithoutTrustingPreRegistrationPassword()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "member@company.example",
            emailConfirmed: false,
            TestContext.Current.CancellationToken);
        var baselineSecurityStamp = member.SecurityStamp;
        await AddPendingEmailConfirmationAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "workspace-link-subject",
                "member@company.example",
                true,
                "company.example",
                null),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var completionResult = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            completionResult.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(storedMember.EmailConfirmed);
        Assert.Null(storedMember.PasswordHash);
        Assert.NotEqual(
            baselineSecurityStamp,
            storedMember.SecurityStamp);
        Assert.Equal(
            "Membre",
            storedMember.DisplayName);
        Assert.NotNull(await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Select(message => message.ProcessedAt)
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            "workspace-link-subject",
            await context.UserLogins
                .AsNoTracking()
                .Select(login => login.ProviderKey)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenPasswordIsCorrect_LinksExactlyAndCreatesAccessAndRefreshTokens()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var user = await CreateLocalMemberAsync(
            factory,
            "member@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var identity = new GoogleIdentity(
            "third-party-subject",
            "member@example.com",
            true,
            null,
            "Member");
        var flowId = Guid.CreateVersion7(_now);
        var authenticationContext = CreateAuthenticationContext(
            identity,
            flowId,
            user.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.Success,
            result.Outcome);
        Assert.NotNull(result.Tokens);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var login = await context.UserLogins
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            user.Id,
            login.UserId);
        Assert.Equal(
            identity.Subject,
            login.ProviderKey);
        Assert.Equal(
            flowId,
            await context.AuthenticationSessions
                .AsNoTracking()
                .Select(session => session.Id)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenAccessTokenCreationFails_RollsBackLinkSessionAndCurrentSessionRevocation()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var logCapture = new CapturingGoogleLoggerProvider();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IAccessTokenService>();
            services.AddSingleton<IAccessTokenService, ThrowingAccessTokenService>();
            services.AddSingleton<ILoggerProvider>(logCapture);
        });
        var user = await CreateLocalMemberAsync(
            factory,
            "member@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var currentRefresh = await CreateCurrentSessionAsync(
            factory,
            user.Id,
            TestContext.Current.CancellationToken);
        var currentSessionId = await ProveCurrentSessionInNewScopeAsync(
            factory,
            currentRefresh,
            TestContext.Current.CancellationToken);

        var authenticationContext = new GoogleAuthenticationContext(
            new GoogleIdentity(
                "third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            false,
            "/",
            Guid.CreateVersion7(_now),
            user.Id,
            currentSessionId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAccountLinkResult> action() => service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            (Func<Task<GoogleAccountLinkResult>>)action);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(session.RevokedAt);
        Assert.DoesNotContain(
            logCapture.Entries,
            entry => entry.Key is >= LogEventIds.GoogleMemberCreated and <=
                LogEventIds.GoogleSessionCreated);
    }

    [Fact]
    public async Task CompleteAsync_WhenFlowIsReplayed_CreatesOneSessionAndRejectsReplay()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "one-time-subject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now),
            null);

        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var firstService =
                firstScope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
            var firstResult = await firstService.CompleteAsync(
                authenticationContext,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                GoogleAuthenticationOutcome.SessionCreated,
                firstResult.Outcome);
        }

        await using var secondScope = factory.Services.CreateAsyncScope();
        var secondService =
            secondScope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => secondService.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
        var context = secondScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Changed name")]
    public async Task CompleteAsync_WhenLinkedSubjectClaimsChange_UsesSubjectWithoutMutatingProfiles(
        string? googleDisplayName)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var firstContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "stable-subject",
                "first@gmail.com"),
            Guid.CreateVersion7(_now),
            null);
        var firstResult = await CompleteInNewScopeAsync(
            factory,
            firstContext,
            TestContext.Current.CancellationToken);
        var memberId = firstResult.MemberId ?? throw new InvalidOperationException(
            "The initial Google member was not returned.");

        var secondContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "stable-subject",
                "changed@gmail.com",
                true,
                null,
                googleDisplayName),
            Guid.CreateVersion7(_now.AddMinutes(1)),
            memberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            secondContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "first@gmail.com",
            user.Email);
        Assert.Equal(
            "Google member",
            user.DisplayName);
        Assert.Equal(
            2,
            await context.AuthenticationSessions.CountAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenLinkedMemberDisappearsAfterSubjectLookup_RejectsWithoutCreatingSession()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IGoogleAccountRepository>();
            services.AddScoped<IGoogleAccountRepository, MissingLinkedMemberGoogleAccountRepository>();
        });
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "missing-linked-subject",
                "missing-linked@gmail.com"),
            Guid.CreateVersion7(_now),
            MissingLinkedMemberGoogleAccountRepository.MissingMemberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.Users
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenNoMemberSnapshotIsFollowedByUnlinkedLocalRegistration_RejectsWithoutClaimingAccount()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var identity = CreateGmailIdentity(
            "snapshot-subject",
            "snapshot@gmail.com");
        var expectedMemberId = await ResolveExpectedMemberInNewScopeAsync(
            factory,
            identity,
            TestContext.Current.CancellationToken);
        Assert.Null(expectedMemberId);
        var localMember = await CreateLocalMemberAsync(
            factory,
            "snapshot@gmail.com",
            emailConfirmed: false,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            identity,
            Guid.CreateVersion7(_now),
            expectedMemberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            localMember.Id,
            storedMember.Id);
        Assert.False(storedMember.EmailConfirmed);
        Assert.NotNull(storedMember.PasswordHash);
        Assert.Equal(
            "Local member",
            storedMember.DisplayName);
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenExpectedLinkedMemberWasDeleted_DoesNotRecreateAccount()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var identity = CreateGmailIdentity(
            "deleted-subject",
            "deleted@gmail.com");
        var initialContext = CreateAuthenticationContext(
            identity,
            Guid.CreateVersion7(_now),
            null);
        var initialResult = await CompleteInNewScopeAsync(
            factory,
            initialContext,
            TestContext.Current.CancellationToken);
        var expectedMemberId = initialResult.MemberId ?? throw new InvalidOperationException(
            "The linked member was not returned.");

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.Users
                .Where(user => user.Id == expectedMemberId)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var replayContext = CreateAuthenticationContext(
            identity,
            Guid.CreateVersion7(_now.AddMinutes(1)),
            expectedMemberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => service.CompleteAsync(
            replayContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.Users
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenExpectedEmailMemberWasRecreated_DoesNotLinkReplacement()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var identity = CreateGmailIdentity(
            "new-subject",
            "recreated@gmail.com");
        var original = await CreateLocalMemberAsync(
            factory,
            "recreated@gmail.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var expectedMemberId = await ResolveExpectedMemberInNewScopeAsync(
            factory,
            identity,
            TestContext.Current.CancellationToken);

        await using (var replacementScope = factory.Services.CreateAsyncScope())
        {
            var replacementContext =
                replacementScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await replacementContext.Users
                .Where(user => user.Id == original.Id)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        var replacement = await CreateLocalMemberAsync(
            factory,
            "recreated@gmail.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            identity,
            Guid.CreateVersion7(_now.AddMinutes(1)),
            expectedMemberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            replacement.Id,
            await context.Users
                .AsNoTracking()
                .Select(user => user.Id)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenSuccessfulFlowIsReplayedWithWrongPassword_DoesNotIncrementFailures()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var user = await CreateLocalMemberAsync(
            factory,
            "member@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "one-shot-link-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            user.Id);

        await using (var successScope = factory.Services.CreateAsyncScope())
        {
            var successService =
                successScope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
            var success = await successService.LinkAsync(
                authenticationContext,
                Password,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                GoogleAccountLinkOutcome.Success,
                success.Outcome);
        }

        await using var replayScope = factory.Services.CreateAsyncScope();
        var replayService =
            replayScope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAccountLinkResult> action() => replayService.LinkAsync(
            authenticationContext,
            "wrong password",
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAccountLinkResult>>)action);
        var context = replayScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            0,
            storedUser.AccessFailedCount);
        Assert.Null(storedUser.LockoutEnd);
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenNewWorkspaceIdentityHasNoName_CreatesPasswordlessMemberWithFallbackName()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "workspace-subject",
                "member@company.example",
                true,
                "company.example",
                null),
            Guid.CreateVersion7(_now),
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "Membre",
            member.DisplayName);
        Assert.True(member.EmailConfirmed);
        Assert.Null(member.PasswordHash);
    }

    [Fact]
    public async Task CompleteAsync_WhenSubjectAndEmailResolveDifferentMembers_UsesSubjectFirst()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var firstContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "stable-subject",
                "first@gmail.com"),
            Guid.CreateVersion7(_now),
            null);
        var setupResult = await CompleteInNewScopeAsync(
            factory,
            firstContext,
            TestContext.Current.CancellationToken);
        var subjectMemberId = setupResult.MemberId ?? throw new InvalidOperationException(
            "The initial Google member was not returned.");

        var emailMember = await CreateLocalMemberAsync(
            factory,
            "second@gmail.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var secondContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "stable-subject",
                "second@gmail.com"),
            Guid.CreateVersion7(_now.AddMinutes(1)),
            subjectMemberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            secondContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            subjectMemberId,
            result.MemberId);
        Assert.Equal(
            GoogleMemberResolution.Found,
            result.MemberResolution);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            2,
            await context.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            subjectMemberId,
            await context.UserLogins
                .AsNoTracking()
                .Select(login => login.UserId)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            emailMember.Id,
            await context.Users
                .AsNoTracking()
                .Where(member => member.Email == "second@gmail.com")
                .Select(member => member.Id)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenGoogleSubjectDiffersOnlyByCase_DoesNotAttachSecondGoogleLogin()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var firstContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "CaseSensitiveSubject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now),
            null);
        var setupResult = await CompleteInNewScopeAsync(
            factory,
            firstContext,
            TestContext.Current.CancellationToken);
        var memberId = setupResult.MemberId ?? throw new InvalidOperationException(
            "The initial Google member was not returned.");

        var secondContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "casesensitivesubject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now.AddMinutes(1)),
            memberId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => service.CompleteAsync(
            secondContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var login = await context.UserLogins
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "CaseSensitiveSubject",
            login.ProviderKey);
        Assert.Equal(
            1,
            await context.AuthenticationSessions.CountAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenLinkedMemberIsUnconfirmed_ConfirmsMemberWithoutReplacingProfile()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "member@gmail.com",
            emailConfirmed: false,
            TestContext.Current.CancellationToken);
        await AddGoogleLoginAsync(
            factory,
            member.Id,
            "linked-subject",
            TestContext.Current.CancellationToken);
        await AddPendingEmailConfirmationAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "linked-subject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(storedMember.EmailConfirmed);
        Assert.Equal(
            "Google member",
            storedMember.DisplayName);
        Assert.Equal(
            "member@gmail.com",
            storedMember.Email);
        Assert.NotNull(await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Select(message => message.ProcessedAt)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("free")]
    [InlineData("unconfirmed")]
    public async Task LinkAsync_WhenThirdPartyCannotProveConfirmedMember_ReturnsInvalidCredentialsWithoutMutation(
        string emailState)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var existingMember = emailState == "unconfirmed"
            ? await CreateLocalMemberAsync(
                factory,
                "member@example.com",
                emailConfirmed: false,
                TestContext.Current.CancellationToken)
            : null;

        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            existingMember?.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.InvalidCredentials,
            result.Outcome);
        Assert.Null(result.Tokens);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));

        if (existingMember is null)
            return;

        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(storedMember.EmailConfirmed);
        Assert.Equal(
            0,
            storedMember.AccessFailedCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenLinkedUnconfirmedMemberHasDifferentEmail_AuthenticatesWithoutConfirmingLocalEmail()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "member@gmail.com",
            emailConfirmed: false,
            TestContext.Current.CancellationToken);
        await AddGoogleLoginAsync(
            factory,
            member.Id,
            "linked-subject",
            TestContext.Current.CancellationToken);
        await AddPendingEmailConfirmationAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "linked-subject",
                "changed@gmail.com"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(storedMember.EmailConfirmed);
        Assert.Equal(
            "member@gmail.com",
            storedMember.Email);
        Assert.Equal(
            "Local member",
            storedMember.DisplayName);
        Assert.Null(await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Select(message => message.ProcessedAt)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenLinkedThirdPartyMemberIsUnconfirmed_AuthenticatesWithoutConfirmingLocalEmail()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "member@example.com",
            emailConfirmed: false,
            TestContext.Current.CancellationToken);
        await AddGoogleLoginAsync(
            factory,
            member.Id,
            "linked-third-party-subject",
            TestContext.Current.CancellationToken);
        await AddPendingEmailConfirmationAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "linked-third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(storedMember.EmailConfirmed);
        Assert.Null(await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Select(message => message.ProcessedAt)
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenLinkedMemberIsLockedOut_RejectsWithoutCreatingSession()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "member@gmail.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        await AddGoogleLoginAsync(
            factory,
            member.Id,
            "locked-subject",
            TestContext.Current.CancellationToken);
        await SetLockoutAsync(
            factory,
            member.Id,
            _now.AddMinutes(15),
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "locked-subject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenConfirmedGmailAccountIsFirstLinked_InvalidatesCredentialSessionsAndLockout()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "locked@gmail.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var baselineSecurityStamp = member.SecurityStamp;
        _ = await CreateCurrentSessionAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        await SetPasswordFailureStateAsync(
            factory,
            member.Id,
            accessFailedCount: 4,
            _now.AddMinutes(5),
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "locked-auto-link-subject",
                "locked@gmail.com"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var googleService = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
        var accountSessionService = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        var result = await googleService.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);
        var passwordLogin = await accountSessionService.LoginAsync(
            "locked@gmail.com",
            Password,
            false,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            passwordLogin.Result);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(storedMember.PasswordHash);
        Assert.NotEqual(
            baselineSecurityStamp,
            storedMember.SecurityStamp);
        AssertSecurityStamp(storedMember.SecurityStamp);
        Assert.Equal(
            0,
            storedMember.AccessFailedCount);
        Assert.Null(storedMember.LockoutEnd);
        Assert.Equal(
            "Local member",
            storedMember.DisplayName);
        Assert.Equal(
            "locked-auto-link-subject",
            await context.UserLogins
                .AsNoTracking()
                .Select(login => login.ProviderKey)
                .SingleAsync(TestContext.Current.CancellationToken));
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            sessions.Length);
        Assert.Single(
            sessions,
            session => session.Id == authenticationContext.FlowId &&
                session.RevokedAt is null);
        Assert.Single(
            sessions,
            session => session.Id != authenticationContext.FlowId &&
                session.RevokedAt == _now.UtcDateTime);
    }

    [Fact]
    public async Task CompleteAsync_WhenAuthoritativeClaimHasPendingEmailChange_RevokesRequestBeforeFutureTokenCanBeDeliveredOrConfirmed()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.ConfigureAuthenticationEmailDelivery();
            services.AddSingleton<IAuthenticationEmailSender, UnexpectedAuthenticationEmailSender>();
            services.AddSingleton<EmailChangeRequestReadCoordinator>();
            services.RemoveAll<IMemberEmailChangeRequestRepository>();
            services.AddScoped<
                IMemberEmailChangeRequestRepository,
                CoordinatedMemberEmailChangeRequestRepository>();
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "member@gmail.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var emailChangeRequestId = await AddPendingEmailChangeAsync(
            factory,
            member.Id,
            "member@gmail.com",
            "attacker@example.com",
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "email-change-reclaim-subject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var dispatchScope = factory.Services.CreateAsyncScope();
        var coordinator = dispatchScope.ServiceProvider
            .GetRequiredService<EmailChangeRequestReadCoordinator>();
        var dispatcher = dispatchScope.ServiceProvider
            .GetRequiredService<IAuthenticationEmailDispatcher>();

        // Act
        var dispatchTask = dispatcher.DispatchPendingAsync(
            new Uri("https://frontend.example"),
            AuthenticationEmailTestPolicy.CreateGoogleConcurrency(),
            TestContext.Current.CancellationToken);
        var requestReadTask = coordinator.WaitUntilRequestReadAsync(
            TestContext.Current.CancellationToken);
        var firstCompletedTask = await Task.WhenAny(
            requestReadTask,
            dispatchTask);

        if (firstCompletedTask == dispatchTask)
            _ = await dispatchTask;

        await requestReadTask;
        GoogleAuthenticationResult result;
        try
        {
            result = await CompleteInNewScopeAsync(
                factory,
                authenticationContext,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            coordinator.Release();
        }

        var dispatchedCount = await dispatchTask;
        await using var assertionScope = factory.Services.CreateAsyncScope();
        var emailChangeService = assertionScope.ServiceProvider
            .GetRequiredService<IMemberEmailChangeService>();
        var userManager = assertionScope.ServiceProvider
            .GetRequiredService<UserManager<MonKadoUser>>();
        var reclaimedMember = await userManager.FindByIdAsync(member.Id.ToString("D"));
        Assert.NotNull(reclaimedMember);
        var purpose = MemberEmailChangeTokenPurpose.Create(
            emailChangeRequestId,
            "ATTACKER@EXAMPLE.COM");
        var token = await userManager.GenerateUserTokenAsync(
            reclaimedMember,
            EmailChangeTokenProviderOptions.ProviderName,
            purpose);
        var confirmationResult = await emailChangeService.ConfirmAsync(
            emailChangeRequestId,
            AuthenticationEmailTokenEncoding.Encode(token),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        Assert.Equal(
            1,
            dispatchedCount);
        Assert.False(confirmationResult);
        var context = assertionScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "member@gmail.com",
            storedMember.Email);
        var storedRequest = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            _now.UtcDateTime,
            storedRequest.RevokedAt);
        Assert.Null(storedRequest.ConfirmedAt);
        var messages = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Where(message => message.MemberEmailChangeRequestId == emailChangeRequestId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Single(messages);
        Assert.All(
            messages,
            message => Assert.Equal(
                _now.UtcDateTime,
                message.ProcessedAt));
    }

    [Fact]
    public async Task LinkAsync_WhenMemberIsLockedOut_ReturnsInvalidCredentialsWithoutIncrementingFailures()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var passwordHasher = new CapturingPasswordHasher();
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IPasswordHasher<MonKadoUser>>();
            services.AddSingleton<IPasswordHasher<MonKadoUser>>(passwordHasher);
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "locked@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var baselineHashCount = passwordHasher.HashCount;
        await SetLockoutAsync(
            factory,
            member.Id,
            _now.AddMinutes(5),
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "locked-link-subject",
                "locked@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.InvalidCredentials,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            0,
            await context.Users
                .AsNoTracking()
                .Select(user => user.AccessFailedCount)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            baselineHashCount + 1,
            passwordHasher.HashCount);
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("subject-linked-to-another-member")]
    [InlineData("member-linked-to-another-subject")]
    public async Task LinkAsync_WhenGoogleLoginConflictsAfterPasswordProof_ReturnsConflictWithoutSession(
        string scenario)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var targetMember = await CreateLocalMemberAsync(
            factory,
            "target@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var requestedSubject = "requested-subject";

        if (scenario == "subject-linked-to-another-member")
        {
            var otherMember = await CreateLocalMemberAsync(
                factory,
                "other@example.com",
                emailConfirmed: true,
                TestContext.Current.CancellationToken);
            await AddGoogleLoginAsync(
                factory,
                otherMember.Id,
                requestedSubject,
                TestContext.Current.CancellationToken);
        }

        if (scenario == "member-linked-to-another-subject")
            await AddGoogleLoginAsync(
                factory,
                targetMember.Id,
                "existing-subject",
                TestContext.Current.CancellationToken);

        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                requestedSubject,
                "target@example.com",
                true,
                null,
                "Target"),
            Guid.CreateVersion7(_now),
            targetMember.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.Conflict,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            0,
            await context.Users
                .AsNoTracking()
                .Where(user => user.Id == targetMember.Id)
                .Select(user => user.AccessFailedCount)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenFlowIsConsumedBetweenLookupAndInsert_RejectsReplayWithoutMutation()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IAuthenticationSessionRepository>();
            services.AddScoped<IAuthenticationSessionRepository, MissingAuthenticationFlowLookupRepository>();
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "replay@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var flowId = Guid.CreateVersion7(_now);

        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var refreshTokenService = setupScope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            var refreshToken = refreshTokenService.Create(flowId);
            setupContext.AuthenticationSessions.Add(AuthenticationSession.Create(
                flowId,
                member.Id,
                refreshToken.Hash,
                false,
                _now.UtcDateTime,
                _now.UtcDateTime.AddHours(8)));
            await setupContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "replay-link-subject",
                "replay@example.com",
                true,
                null,
                "Member"),
            flowId,
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAccountLinkResult> action() => service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(
            (Func<Task<GoogleAccountLinkResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenConcurrentLoginConstraintWins_ReturnsConflictWithoutSecondLogin()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IGoogleAccountRepository>();
            services.AddScoped<IGoogleAccountRepository, MissGoogleLoginLookupRepository>();
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "constraint@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        await AddGoogleLoginAsync(
            factory,
            member.Id,
            "existing-constraint-subject",
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "concurrent-constraint-subject",
                "constraint@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.Conflict,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            "existing-constraint-subject",
            await context.UserLogins
                .AsNoTracking()
                .Select(login => login.ProviderKey)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenDbUpdateIsNotFromPostgreSql_DoesNotMisclassifyFailure()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IUnitOfWork>();
            services.AddScoped<IUnitOfWork, NonPostgreSqlDbUpdateUnitOfWork>();
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "technical@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "technical-subject",
                "technical@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAccountLinkResult> action() => service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(
            (Func<Task<GoogleAccountLinkResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenFivePasswordsAreInvalid_ProgressesToLockoutWithoutCreatingSession()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "member@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);

        // Act
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using var attemptScope = factory.Services.CreateAsyncScope();
            var service =
                attemptScope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
            var result = await service.LinkAsync(
                authenticationContext,
                "wrong password",
                TestContext.Current.CancellationToken);
            Assert.Equal(
                GoogleAccountLinkOutcome.InvalidCredentials,
                result.Outcome);
            var attemptContext =
                attemptScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var attemptState = await attemptContext.Users
                .AsNoTracking()
                .Where(user => user.Id == member.Id)
                .Select(user => new
                {
                    user.AccessFailedCount,
                    user.LockoutEnd
                })
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(
                attempt == 5
                    ? 0
                    : attempt,
                attemptState.AccessFailedCount);
            Assert.Equal(
                attempt == 5,
                attemptState.LockoutEnd is not null);
        }

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            0,
            storedMember.AccessFailedCount);
        Assert.NotNull(storedMember.LockoutEnd);
        Assert.Empty(await context.UserLogins
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenIdentityCannotRecordFailure_RollsBackWithoutIncrementingFailures()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var setupFactory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            setupFactory,
            "member@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<UserManager<MonKadoUser>>();
            services.AddScoped<UserManager<MonKadoUser>, FailingIdentityUpdateUserManager>();
        });
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAccountLinkResult> action() => service.LinkAsync(
            authenticationContext,
            "wrong password",
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            (Func<Task<GoogleAccountLinkResult>>)action);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            0,
            storedMember.AccessFailedCount);
        Assert.Null(storedMember.LockoutEnd);
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenCurrentSessionIsProven_RevokesOnlyThatDeviceSession()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var member = await CreateLocalMemberAsync(
            factory,
            "member@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var currentRefresh = await CreateCurrentSessionAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        _ = await CreateCurrentSessionAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var currentSessionId = await ProveCurrentSessionInNewScopeAsync(
            factory,
            currentRefresh,
            TestContext.Current.CancellationToken);

        var authenticationContext = new GoogleAuthenticationContext(
            new GoogleIdentity(
                "third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            false,
            "/",
            Guid.CreateVersion7(_now.AddMinutes(1)),
            member.Id,
            currentSessionId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.Success,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            3,
            sessions.Length);
        Assert.Single(
            sessions,
            session => session.Id == currentSessionId && session.RevokedAt is not null);
        Assert.Equal(
            2,
            sessions.Count(session => session.RevokedAt is null));
    }

    [Fact]
    public async Task CompleteAsync_WhenDistinctFlowsRaceForSameIdentity_CreatesOneMemberAndOneSessionPerFlow()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var identity = CreateGmailIdentity(
            "concurrent-subject",
            "concurrent@gmail.com");
        var firstFlowId = Guid.CreateVersion7(_now);
        var secondFlowId = Guid.CreateVersion7(_now.AddMilliseconds(1));

        // Act
        var results = await Task.WhenAll(
            CompleteInNewScopeAsync(
                factory,
                CreateAuthenticationContext(
                    identity,
                    firstFlowId,
                    null),
                TestContext.Current.CancellationToken),
            CompleteInNewScopeAsync(
                factory,
                CreateAuthenticationContext(
                    identity,
                    secondFlowId,
                    null),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.All(
            results,
            result => Assert.Equal(
                GoogleAuthenticationOutcome.SessionCreated,
                result.Outcome));
        Assert.Equal(
            results[0].MemberId,
            results[1].MemberId);
        Assert.NotEqual(
            results[0].Session?.RefreshToken,
            results[1].Session?.RefreshToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await context.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await context.UserLogins.CountAsync(TestContext.Current.CancellationToken));
        var sessionIds = await context.AuthenticationSessions
            .AsNoTracking()
            .Select(session => session.Id)
            .OrderBy(sessionId => sessionId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                firstFlowId,
                secondFlowId
            ],
            sessionIds);
    }

    [Fact]
    public async Task CompleteAsync_WhenSubjectLinkAppearsAfterInitialLookup_UsesMatchingSubjectMember()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IGoogleAccountRepository>();
            services.AddScoped<IGoogleAccountRepository, MissFirstGoogleSubjectLookupRepository>();
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "appeared@gmail.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        await AddGoogleLoginAsync(
            factory,
            member.Id,
            "appeared-subject",
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "appeared-subject",
                "appeared@gmail.com"),
            Guid.CreateVersion7(_now),
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        Assert.Equal(
            member.Id,
            result.MemberId);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenSameFlowIsSubmittedConcurrently_AllowsExactlyOneSession()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var flowId = Guid.CreateVersion7(_now);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "same-flow-subject",
                "same-flow@gmail.com"),
            flowId,
            null);
        var attempts = (Task<GoogleAuthenticationResult>[])
        [
            CompleteInNewScopeAsync(
                factory,
                authenticationContext,
                TestContext.Current.CancellationToken),
            CompleteInNewScopeAsync(
                factory,
                authenticationContext,
                TestContext.Current.CancellationToken)
        ];

        // Act
        try
        {
            await Task.WhenAll(attempts);
        }
        catch (GoogleAuthenticationFailedException)
        {
        }

        // Assert
        var successfulAttempt = Assert.Single(
            attempts,
            attempt => attempt.IsCompletedSuccessfully);
        var failedAttempt = Assert.Single(
            attempts,
            attempt => attempt.IsFaulted);
        Assert.NotNull(failedAttempt.Exception);
        Assert.IsType<GoogleAuthenticationFailedException>(
            failedAttempt.Exception.GetBaseException());
        var successfulResult = await successfulAttempt;
        Assert.NotNull(successfulResult.Session);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            flowId,
            session.Id);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                successfulResult.Session.RefreshToken)),
            session.RefreshTokenHash);
    }

    [Fact]
    public async Task CompleteAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IMonKadoUserRepository>();
            services.AddScoped<IMonKadoUserRepository, UnavailableMonKadoUserRepository>();
        });
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "unavailable-subject",
                "member@gmail.com"),
            Guid.CreateVersion7(_now),
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAuthenticationResult> action() => service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(
            (Func<Task<GoogleAuthenticationResult>>)action);
    }

    [Fact]
    public async Task ResolveExpectedMemberIdAsync_WhenSubjectAndEmailResolveDifferentMembers_ReturnsSubjectMember()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory();
        var subjectMember = await CreateLocalMemberAsync(
            factory,
            "subject@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        _ = await CreateLocalMemberAsync(
            factory,
            "email@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        await AddGoogleLoginAsync(
            factory,
            subjectMember.Id,
            "resolved-subject",
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        var memberId = await service.ResolveExpectedMemberIdAsync(
            new GoogleIdentity(
                "resolved-subject",
                "email@example.com",
                true,
                null,
                "Member"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            subjectMember.Id,
            memberId);
    }

    [Fact]
    public async Task ResolveExpectedMemberIdAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IMonKadoUserRepository>();
            services.AddScoped<IMonKadoUserRepository, UnavailableMonKadoUserRepository>();
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<Guid?> action() => service.ResolveExpectedMemberIdAsync(
            new GoogleIdentity(
                "unavailable-resolution-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(
            (Func<Task<Guid?>>)action);
    }

    [Fact]
    public async Task LinkAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IMonKadoUserRepository>();
            services.AddScoped<IMonKadoUserRepository, UnavailableMonKadoUserRepository>();
        });
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "unavailable-link-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        // Act
        Task<GoogleAccountLinkResult> action() => service.LinkAsync(
            authenticationContext,
            Password,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(
            (Func<Task<GoogleAccountLinkResult>>)action);
    }

    [Fact]
    public async Task CompleteAsync_WhenCommitAcknowledgementIsLost_ReturnsOriginalRefreshSecretOnce()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = CreateFactory(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var flowId = Guid.CreateVersion7(_now);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "acknowledgement-subject",
                "acknowledgement@gmail.com"),
            flowId,
            null);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
        interceptor.Arm();

        // Act
        var result = await service.CompleteAsync(
            authenticationContext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAuthenticationOutcome.SessionCreated,
            result.Outcome);
        Assert.NotNull(result.Session);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            flowId,
            session.Id);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(result.Session.RefreshToken)),
            session.RefreshTokenHash);
    }

    [Fact]
    public async Task LinkAsync_WhenInvalidPasswordCommitAcknowledgementIsLost_IncrementsFailureOnlyOnce()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = CreateFactory(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "member@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "third-party-subject",
                "member@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
        interceptor.Arm();

        // Act
        var result = await service.LinkAsync(
            authenticationContext,
            "wrong password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.InvalidCredentials,
            result.Outcome);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            1,
            storedMember.AccessFailedCount);
        Assert.Null(storedMember.LockoutEnd);
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinkAsync_WhenConcurrentInvalidPasswordCommitsBeforeAmbiguousResult_DoesNotReplayFirstFailure()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        await using var factory = CreateFactory(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var member = await CreateLocalMemberAsync(
            factory,
            "concurrent-password@example.com",
            emailConfirmed: true,
            TestContext.Current.CancellationToken);
        var authenticationContext = CreateAuthenticationContext(
            new GoogleIdentity(
                "concurrent-password-subject",
                "concurrent-password@example.com",
                true,
                null,
                "Member"),
            Guid.CreateVersion7(_now),
            member.Id);
        interceptor.Arm();
        var ambiguousAttempt = LinkInNewScopeAsync(
            factory,
            authenticationContext,
            "first wrong password",
            TestContext.Current.CancellationToken);
        await interceptor.WaitForFirstCommitAsync(
            TestContext.Current.CancellationToken);

        // Act
        var concurrentResult = await LinkInNewScopeAsync(
            factory,
            authenticationContext,
            "second wrong password",
            TestContext.Current.CancellationToken);
        interceptor.ReleaseFailure();
        var ambiguousResult = await ambiguousAttempt;

        // Assert
        Assert.Equal(
            GoogleAccountLinkOutcome.InvalidCredentials,
            concurrentResult.Outcome);
        Assert.Equal(
            GoogleAccountLinkOutcome.InvalidCredentials,
            ambiguousResult.Outcome);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            storedMember.AccessFailedCount);
        Assert.Null(storedMember.LockoutEnd);
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenRolledBackAttemptObservesConcurrentWinner_DoesNotReturnLosingRefreshSecret()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var interceptor = new ConcurrentWinnerCommitInterceptor();
        await using var factory = CreateFactory(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var flowId = Guid.CreateVersion7(_now);
        var authenticationContext = CreateAuthenticationContext(
            CreateGmailIdentity(
                "coordinated-subject",
                "coordinated@gmail.com"),
            flowId,
            null);
        interceptor.Arm();
        var losingAttempt = CompleteInNewScopeAsync(
            factory,
            authenticationContext,
            TestContext.Current.CancellationToken);
        await interceptor.WaitForFirstCommitAttemptAsync(
            TestContext.Current.CancellationToken);

        // Act
        var winningResult = await CompleteInNewScopeAsync(
            factory,
            authenticationContext,
            TestContext.Current.CancellationToken);
        interceptor.ReleaseVerification();
        Task losingAction() => losingAttempt;

        // Assert
        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(losingAction);
        Assert.NotNull(winningResult.Session);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            flowId,
            session.Id);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(winningResult.Session.RefreshToken)),
            session.RefreshTokenHash);
    }

    private PostgreSqlApiFactory CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {

        return new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(_now),
            configureServices: configureServices);
    }

    private static GoogleAuthenticationContext CreateAuthenticationContext(
        GoogleIdentity identity,
        Guid flowId,
        Guid? expectedMemberId,
        bool isPersistent = false)
    {

        return new GoogleAuthenticationContext(
            identity,
            isPersistent,
            "/my-lists",
            flowId,
            expectedMemberId,
            null);
    }

    private static GoogleIdentity CreateGmailIdentity(
        string subject,
        string email)
    {

        return new GoogleIdentity(
            subject,
            email,
            true,
            null,
            "Google member");
    }

    private static void AssertSecurityStamp(string? securityStamp)
    {
        Assert.NotNull(securityStamp);
        Assert.Equal(
            27,
            securityStamp.Length);
        Assert.Matches(
            "^[A-Za-z0-9_-]{27}$",
            securityStamp);
    }

    private static async Task<MonKadoUser> CreateLocalMemberAsync(
        PostgreSqlApiFactory factory,
        string email,
        bool emailConfirmed,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_now),
            Email = email,
            UserName = email,
            DisplayName = "Local member",
            EmailConfirmed = emailConfirmed,
            UnconfirmedAccountExpiresAt = emailConfirmed
                ? null
                : _now.UtcDateTime.AddDays(30)
        };
        cancellationToken.ThrowIfCancellationRequested();
        var result = await userManager.CreateAsync(
            user,
            Password);
        Assert.True(
            result.Succeeded,
            string.Join(
                ", ",
                result.Errors.Select(error => error.Code)));

        return user;
    }

    private static async Task<string> CreateCurrentSessionAsync(
        PostgreSqlApiFactory factory,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var sessionId = Guid.CreateVersion7(_now);
        var refreshToken = refreshTokenService.Create(sessionId);
        context.AuthenticationSessions.Add(AuthenticationSession.Create(
            sessionId,
            userId,
            refreshToken.Hash,
            false,
            _now.UtcDateTime,
            _now.UtcDateTime.AddHours(8)));
        await context.SaveChangesAsync(cancellationToken);

        return refreshToken.Value;
    }

    private static async Task AddGoogleLoginAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        string subject,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.UserLogins.Add(new IdentityUserLogin<Guid>
        {
            LoginProvider = ExternalLoginProviders.Google,
            ProviderKey = subject,
            ProviderDisplayName = ExternalLoginProviders.Google,
            UserId = memberId
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SetLockoutAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        DateTimeOffset lockoutEnd,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.LockoutEnd,
                    lockoutEnd),
                cancellationToken);
    }

    private static async Task SetPasswordFailureStateAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        int accessFailedCount,
        DateTimeOffset lockoutEnd,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        member => member.AccessFailedCount,
                        accessFailedCount)
                    .SetProperty(
                        member => member.LockoutEnd,
                        lockoutEnd),
                cancellationToken);
    }

    private static async Task AddPendingEmailConfirmationAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.AuthenticationEmailOutboxMessages.Add(
            AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
                memberId,
                _now.UtcDateTime));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Guid> AddPendingEmailChangeAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        string currentEmail,
        string newEmail,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = MemberEmailChangeRequest.Create(
            memberId,
            currentEmail,
            newEmail,
            newEmail.ToUpperInvariant(),
            _now.UtcDateTime,
            _now.UtcDateTime.AddDays(1));
        var securityStamp = await context.Users
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(member => member.SecurityStamp)
            .SingleAsync(cancellationToken);
        Assert.NotNull(securityStamp);
        context.MemberEmailChangeRequests.Add(request);
        context.AuthenticationEmailOutboxMessages.Add(
            AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
                request.Id,
                memberId,
                newEmail,
                securityStamp,
                _now.UtcDateTime));
        await context.SaveChangesAsync(cancellationToken);

        return request.Id;
    }

    private static async Task<GoogleAuthenticationResult> CompleteInNewScopeAsync(
        PostgreSqlApiFactory factory,
        GoogleAuthenticationContext authenticationContext,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        return await service.CompleteAsync(
            authenticationContext,
            cancellationToken);
    }

    private static async Task<GoogleAccountLinkResult> LinkInNewScopeAsync(
        PostgreSqlApiFactory factory,
        GoogleAuthenticationContext authenticationContext,
        string currentPassword,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        return await service.LinkAsync(
            authenticationContext,
            currentPassword,
            cancellationToken);
    }

    private static async Task<Guid?> ProveCurrentSessionInNewScopeAsync(
        PostgreSqlApiFactory factory,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshSessionService>();

        return await service.ProveCurrentSessionAsync(
            refreshToken,
            cancellationToken);
    }

    private static async Task<Guid?> ResolveExpectedMemberInNewScopeAsync(
        PostgreSqlApiFactory factory,
        GoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        return await service.ResolveExpectedMemberIdAsync(
            identity,
            cancellationToken);
    }
}
