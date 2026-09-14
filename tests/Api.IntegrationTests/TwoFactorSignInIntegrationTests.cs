using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class TwoFactorSignInIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "A deliberately long test password";

    [Theory]
    [InlineData("/api/v1/members/current/two-factor/reauthentications")]
    [InlineData("/api/v1/auth/two-factor/recovery-codes/regenerations")]
    public async Task ManageAsync_WhenOnlyValidRefreshCookieIsPresent_RejectsWithoutChangingCredentials(string endpoint)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        var enrollment = await EnrollAndSignInAsync(
            client,
            email,
            clock,
            cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            enrollment.Tokens.AccessToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        using var authorization = await client.PostAsJsonAsync(
            "/api/v1/members/current/two-factor/reauthentications",
            new
            {
                purpose = "regenerateRecoveryCodes",
                code = TwoFactorTestData.CreateCurrentCode(
                    enrollment.ManualKey,
                    clock)
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            authorization.StatusCode);
        var grant = await authorization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        using var response = await client.PostAsJsonAsync(
            endpoint,
            new
            {
                flow = grant.GetProperty("flow").GetString(),
                purpose = "replaceAuthenticator",
                recoveryCode = enrollment.Codes.RecoveryCodes.First()
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            10,
            await database.TwoFactorRecoveryCodes.CountAsync(
                code => code.ConsumedAt == null,
                cancellationToken));
        Assert.False(await database.AuthenticationSessions.AnyAsync(
            session => session.RevokedAt != null,
            cancellationToken));
        using var refresh = await PostAsync(
            client,
            "/api/v1/auth/sessions/refresh",
            new { },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            refresh.StatusCode);
    }

    [Fact]
    public async Task ReauthenticateAsync_WhenPurposeDoesNotMatchProof_RejectsWithoutConsumingRecoveryCodeOrGrant()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        var enrollment = await EnrollAndSignInAsync(
            client,
            email,
            clock,
            cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            enrollment.Tokens.AccessToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var token = await database.AuthenticationAccessTokens.SingleAsync(cancellationToken);
        var memberId = await database.Users.Select(member => member.Id).SingleAsync(cancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.ReauthenticateAsync(
            memberId,
            token.Id,
            TwoFactorFlowPurpose.SignIn,
            null,
            enrollment.Codes.RecoveryCodes.First(),
            cancellationToken));
        clock.Advance(TimeSpan.FromSeconds(30));
        using var authorization = await client.PostAsJsonAsync(
            "/api/v1/members/current/two-factor/reauthentications",
            new
            {
                purpose = "replaceAuthenticator",
                code = TwoFactorTestData.CreateCurrentCode(
                    enrollment.ManualKey,
                    clock)
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            authorization.StatusCode);
        var grant = await authorization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var flow = grant.GetProperty("flow").GetString();
        using var signIn = await PostAsync(
            client,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow
            },
            cancellationToken);
        using var regeneration = await client.PostAsJsonAsync(
            "/api/v1/auth/two-factor/recovery-codes/regenerations",
            new
            {
                flow
            },
            cancellationToken);

        // Assert
        Assert.IsType<TwoFactorOperationConflictException>(exception);
        Assert.Equal(
            HttpStatusCode.Conflict,
            signIn.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            regeneration.StatusCode);
        Assert.False(await database.TwoFactorRecoveryCodes.AnyAsync(
            code => code.ReservedChallengeId != null || code.ConsumedAt != null,
            cancellationToken));
        var stored = await database.TwoFactorChallenges.AsNoTracking().SingleAsync(
            challenge => challenge.Purpose == TwoFactorFlowPurpose.ReplaceAuthenticator,
            cancellationToken);
        Assert.Null(stored.ConsumedAt);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("anotherSession")]
    [InlineData("anotherMember")]
    [InlineData("revokedSession")]
    public async Task GetSetupAsync_WhenManagementGrantIsUsedOutsideOriginalSession_RejectsWithoutStagingCandidate(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var original = factory.CreateClient();
        var enrollment = await EnrollAndSignInAsync(
            original,
            email,
            clock,
            cancellationToken);
        original.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            enrollment.Tokens.AccessToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        using var authorization = await original.PostAsJsonAsync(
            "/api/v1/members/current/two-factor/reauthentications",
            new
            {
                purpose = "replaceAuthenticator",
                code = TwoFactorTestData.CreateCurrentCode(
                    enrollment.ManualKey,
                    clock)
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            authorization.StatusCode);
        var grant = await authorization.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var flow = grant.GetProperty("flow").GetString();
        using var caller = factory.CreateClient();

        if (scenario == "anotherSession")
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            var signIn = await StartSignInAsync(
                caller,
                email,
                cancellationToken);
            using var completed = await PostAsync(
                caller,
                "/api/v1/auth/two-factor/completions",
                new
                {
                    flow = signIn.Flow,
                    code = TwoFactorTestData.CreateCurrentCode(
                        enrollment.ManualKey,
                        clock)
                },
                cancellationToken);
            var tokens = await completed.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken);
            Assert.NotNull(tokens);
            caller.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                tokens.AccessToken);
        }

        if (scenario == "anotherMember")
        {
            var otherMemberId = await ReportedWishlistTestData.CreateOwnerAsync(
                factory,
                cancellationToken);
            using var otherMember = await AuthenticationTestData.CreateClientAsync(
                factory,
                otherMemberId,
                cancellationToken);
            caller.DefaultRequestHeaders.Authorization = otherMember.DefaultRequestHeaders.Authorization;
        }

        if (scenario == "revokedSession")
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await database.AuthenticationSessions.ExecuteUpdateAsync(
                setters => setters.SetProperty(session => session.RevokedAt, clock.GetUtcNow().UtcDateTime),
                cancellationToken);
        }

        // Act
        using var response = await PostAsync(
            caller,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var verification = factory.Services.CreateAsyncScope();
        var store = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var challenge = await store.TwoFactorChallenges.SingleAsync(
            candidate => candidate.Purpose == TwoFactorFlowPurpose.ReplaceAuthenticator,
            cancellationToken);
        Assert.Null(challenge.PendingCredentialId);
        Assert.Null(challenge.PendingProtectedSecret);
        Assert.Null(challenge.ConsumedAt);
    }

    [Fact]
    public async Task ConfirmSetupAsync_WhenClientSkipsStepsOrProvidesIncorrectCode_PreservesPendingEnrollmentForValidRetry()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        var challenge = await StartSignInAsync(
            client,
            email,
            cancellationToken);

        // Act
        using var premature = await PostAsync(
            client,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = challenge.Flow,
                code = "123456"
            },
            cancellationToken);
        using var missingSetup = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup/confirmations",
            new
            {
                flow = challenge.Flow,
                code = "123456"
            },
            cancellationToken);
        using var setupResponse = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);
        var setup = await setupResponse.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(cancellationToken);
        Assert.NotNull(setup);
        using var invalidCode = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup/confirmations",
            new
            {
                flow = challenge.Flow,
                code = TwoFactorTestData.CreateIncorrectCode(
                    setup.ManualKey,
                    clock)
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            premature.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            missingSetup.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            invalidCode.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var factor = await database.MemberTwoFactors.SingleAsync(cancellationToken);
            Assert.Null(factor.CredentialId);
            Assert.Equal(
                1,
                factor.FailedAttempts);
            Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        }

        var enrolled = await ConfirmEnrollmentAsync(
            client,
            challenge.Flow,
            clock,
            cancellationToken);
        Assert.Equal(
            setup.ManualKey,
            enrolled.ManualKey);
        Assert.Equal(
            10,
            enrolled.Codes.RecoveryCodes.Count());
    }

    [Theory]
    [InlineData("revokedSession")]
    [InlineData("deletedReceipt")]
    [InlineData("changedStamp")]
    public async Task CompleteAsync_WhenSecurityStateChangesBeforeCommitRecovery_DoesNotReturnObsoleteTokens(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var interceptor = new CoordinatedAmbiguousCommitInterceptor();
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock,
            configureServices: services => services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor)));
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        var challenge = await StartSignInAsync(
            client,
            email,
            cancellationToken);
        await ConfirmEnrollmentAsync(
            client,
            challenge.Flow,
            clock,
            cancellationToken);
        interceptor.Arm();

        // Act
        var completion = PostAsync(
            client,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);
        await interceptor.WaitForFirstCommitAsync(cancellationToken);
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            if (scenario == "deletedReceipt")
                await database.TwoFactorChallenges.ExecuteDeleteAsync(cancellationToken);
            else if (scenario == "changedStamp")
                await database.Users.ExecuteUpdateAsync(
                    setters => setters.SetProperty(member => member.SecurityStamp, "security-event-after-commit"),
                    cancellationToken);
            else
                await database.AuthenticationSessions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(session => session.RevokedAt, clock.GetUtcNow().UtcDateTime),
                    cancellationToken);
        }
        finally
        {
            interceptor.ReleaseFailure();
        }
        using var response = await completion;

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(
            "TECHNICAL_DEPENDENCY_UNAVAILABLE",
            error.GetProperty("errorCode").GetString());
        Assert.False(error.TryGetProperty(
            "accessToken",
            out _));
    }

    [Theory]
    [InlineData("/api/v1/auth/two-factor/setup")]
    [InlineData("/api/v1/auth/two-factor/setup/confirmations")]
    [InlineData("/api/v1/auth/two-factor/completions")]
    public async Task PostAsync_WhenEnrollmentFlowReachesAbsoluteExpiry_RejectsItWithoutCreatingCredentials(string path)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        var challenge = await StartSignInAsync(
            client,
            email,
            cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(5));
        var body = path.EndsWith("/setup", StringComparison.Ordinal)
            ? (object)new
            {
                flow = challenge.Flow
            }
            : new
            {
                flow = challenge.Flow,
                code = "123456"
            };

        // Act
        using var response = await PostAsync(
            client,
            path,
            body,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.False(await database.AuthenticationAccessTokens.AnyAsync(cancellationToken));
        Assert.False(await database.TwoFactorRecoveryCodes.AnyAsync(cancellationToken));
        var factor = await database.MemberTwoFactors.SingleAsync(cancellationToken);
        Assert.Null(factor.CredentialId);
        var stored = await database.TwoFactorChallenges.SingleAsync(cancellationToken);
        Assert.Equal(
            challenge.ExpiresAt,
            stored.ExpiresAt);
    }

    [Fact]
    public async Task CompleteAsync_WhenRecoveryReservationExpires_RejectsOldFlowAndAllowsNewRecoveryWithoutChangingFactor()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var original = factory.CreateClient();
        var enrollment = await EnrollAndSignInAsync(
            original,
            email,
            clock,
            cancellationToken);
        original.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            enrollment.Tokens.AccessToken);
        using var recovering = factory.CreateClient();
        var first = await StartSignInAsync(
            recovering,
            email,
            cancellationToken);
        var second = await StartSignInAsync(
            recovering,
            email,
            cancellationToken);
        var recoveryCode = enrollment.Codes.RecoveryCodes.First();
        using var reserved = await PostAsync(
            recovering,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = first.Flow,
                recoveryCode
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Accepted,
            reserved.StatusCode);

        // Act
        using var competing = await PostAsync(
            recovering,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = second.Flow,
                recoveryCode
            },
            cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(5));
        using var expired = await PostAsync(
            recovering,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow = first.Flow
            },
            cancellationToken);
        var next = await StartSignInAsync(
            recovering,
            email,
            cancellationToken);
        using var recovered = await PostAsync(
            recovering,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = next.Flow,
                recoveryCode
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            competing.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            expired.StatusCode);
        var replacement = await ReadChallengeAsync(
            recovered,
            cancellationToken);
        Assert.Equal(
            TwoFactorRequiredAction.Replace,
            replacement.RequiredAction);
        Assert.Equal(
            next.ExpiresAt,
            replacement.ExpiresAt);
        using var current = await original.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await database.AuthenticationSessions.CountAsync(cancellationToken));
        Assert.Equal(
            10,
            await database.TwoFactorRecoveryCodes.CountAsync(
                code => code.ConsumedAt == null,
                cancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenTwoFlowsUseTheSameTotp_OnlyOneSessionIsIssued()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var enrolledClient = factory.CreateClient();
        var enrolled = await EnrollAndSignInAsync(
            enrolledClient,
            email,
            clock,
            cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(30));
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var first = await StartSignInAsync(
            firstClient,
            email,
            cancellationToken);
        var second = await StartSignInAsync(
            secondClient,
            email,
            cancellationToken);
        var code = TwoFactorTestData.CreateCurrentCode(
            enrolled.ManualKey,
            clock);

        // Act
        var responses = await Task.WhenAll(
            PostAsync(
                firstClient,
                "/api/v1/auth/two-factor/completions",
                new
                {
                    flow = first.Flow,
                    code
                },
                cancellationToken),
            PostAsync(
                secondClient,
                "/api/v1/auth/two-factor/completions",
                new
                {
                    flow = second.Flow,
                    code
                },
                cancellationToken));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];

        // Assert
        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Unauthorized],
            responses.Select(response => response.StatusCode).Order());
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            2,
            await database.AuthenticationSessions.CountAsync(cancellationToken));
        Assert.Equal(
            2,
            await database.AuthenticationAccessTokens.CountAsync(cancellationToken));
        var factor = await database.MemberTwoFactors.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            clock.GetUtcNow().ToUnixTimeSeconds() / 30,
            factor.LastAcceptedTimeStep);
        Assert.Equal(
            1,
            factor.FailedAttempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmSetupAsync_WhenAuthenticatedReplacementIsAuthorized_ConsumesManagementGrantWithoutCreatingSession(bool useRecoveryCode)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        var enrolled = await EnrollAndSignInAsync(
            client,
            email,
            clock,
            cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            enrolled.Tokens.AccessToken);
        clock.Advance(TimeSpan.FromSeconds(30));

        // Act
        using var reauthentication = await client.PostAsJsonAsync(
            "/api/v1/members/current/two-factor/reauthentications",
            new
            {
                purpose = "replaceAuthenticator",
                code = useRecoveryCode ? null : TwoFactorTestData.CreateCurrentCode(
                    enrolled.ManualKey,
                    clock),
                recoveryCode = useRecoveryCode ? enrolled.Codes.RecoveryCodes.First() : null
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            reauthentication.StatusCode);
        var grant = await reauthentication.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var flow = grant.GetProperty("flow").GetString();
        Assert.NotNull(flow);
        var replacement = await ConfirmEnrollmentAsync(
            client,
            flow,
            clock,
            cancellationToken);
        Assert.NotEqual(
            enrolled.ManualKey,
            replacement.ManualKey);
        using var revoked = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            revoked.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await database.AuthenticationSessions.CountAsync(cancellationToken));
        Assert.True(await database.AuthenticationSessions.AllAsync(
            session => session.RevokedAt != null,
            cancellationToken));
        Assert.Equal(
            1,
            await database.TwoFactorChallenges.CountAsync(
                challenge => challenge.Purpose == TwoFactorFlowPurpose.ReplaceAuthenticator && challenge.ConsumedAt != null,
                cancellationToken));
        Assert.Equal(
            useRecoveryCode ? 1 : 0,
            await database.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.Kind == AuthenticationEmailKind.TwoFactorRecoveryCodeUsed,
                cancellationToken));
    }

    [Theory]
    [InlineData("login")]
    [InlineData("setup")]
    [InlineData("confirmation")]
    [InlineData("completion")]
    public async Task CompleteAsync_WhenCommitAcknowledgementIsLost_ConfirmsExactCodesAndRegisteredTokenWithoutReplay(string lostAt)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var interceptor = new AmbiguousCommitInterceptor();
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock,
            configureServices: services => services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor)));
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();

        if (lostAt == "login")
            interceptor.Arm();

        var challenge = await StartSignInAsync(
            client,
            email,
            cancellationToken);

        if (lostAt == "setup")
            interceptor.Arm();

        using var setupResponse = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);
        var setup = await setupResponse.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(cancellationToken);
        Assert.NotNull(setup);

        // Act

        if (lostAt == "confirmation")
            interceptor.Arm();

        using var confirmation = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup/confirmations",
            new
            {
                flow = challenge.Flow,
                code = TwoFactorTestData.CreateCurrentCode(
                    setup.ManualKey,
                    clock)
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            confirmation.StatusCode);

        if (lostAt == "completion")
            interceptor.Arm();

        using var completion = await PostAsync(
            client,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            completion.StatusCode);
        var codes = await confirmation.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesResponse>(cancellationToken);
        Assert.NotNull(codes);
        var tokens = await completion.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken);
        Assert.NotNull(tokens);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<JennGllg.Fr.MonKado.Back.Application.Abstractions.ITwoFactorCryptography>();
        var storedCodes = await database.TwoFactorRecoveryCodes.AsNoTracking().ToArrayAsync(cancellationToken);
        Assert.Equal(
            10,
            storedCodes.Length);
        Assert.All(
            codes.RecoveryCodes,
            code => Assert.Contains(
                storedCodes,
                stored => stored.CodeHash.SequenceEqual(crypto.HashRecoveryCode(code))));
        Assert.Equal(
            1,
            await database.AuthenticationSessions.CountAsync(cancellationToken));
        Assert.Equal(
            1,
            await database.AuthenticationAccessTokens.CountAsync(cancellationToken));
        Assert.Equal(
            1,
            await database.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.Kind == AuthenticationEmailKind.TwoFactorEnrolled,
                cancellationToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.AccessToken);
        using var current = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
    }

    [Fact]
    public async Task CompleteAsync_WhenRecoveryCodeIsUsed_RequiresReplacementBeforeIssuingSessionAndRevokesOldProofs()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var original = factory.CreateClient();
        var enrolled = await EnrollAndSignInAsync(
            original,
            email,
            clock,
            cancellationToken);
        original.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            enrolled.Tokens.AccessToken);
        using var recovering = factory.CreateClient();
        var challenge = await StartSignInAsync(
            recovering,
            email,
            cancellationToken);

        // Act
        using var recovery = await PostAsync(
            recovering,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = challenge.Flow,
                recoveryCode = enrolled.Codes.RecoveryCodes.First()
            },
            cancellationToken);
        var replacement = await ReadChallengeAsync(
            recovery,
            cancellationToken);

        // Assert
        Assert.Equal(
            TwoFactorRequiredAction.Replace,
            replacement.RequiredAction);
        Assert.Equal(
            challenge.ExpiresAt,
            replacement.ExpiresAt);
        Assert.False(recovery.Headers.Contains("Set-Cookie"));
        using var stillAuthenticated = await original.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            stillAuthenticated.StatusCode);
        using var premature = await PostAsync(
            recovering,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Conflict,
            premature.StatusCode);

        var updated = await ConfirmEnrollmentAsync(
            recovering,
            challenge.Flow,
            clock,
            cancellationToken);
        Assert.DoesNotContain(
            updated.Codes.RecoveryCodes,
            code => enrolled.Codes.RecoveryCodes.Contains(code));
        using var revoked = await original.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            revoked.StatusCode);
        using var completed = await PostAsync(
            recovering,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            completed.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var kinds = await database.AuthenticationEmailOutboxMessages
            .OrderBy(message => message.Kind)
            .Select(message => message.Kind)
            .ToArrayAsync(cancellationToken);
        Assert.Equal(
            [
                AuthenticationEmailKind.TwoFactorEnrolled,
                AuthenticationEmailKind.TwoFactorRecoveryCodeUsed,
                AuthenticationEmailKind.TwoFactorReplaced
            ],
            kinds);
        Assert.Equal(
            1,
            await database.AuthenticationSessions.CountAsync(
                session => session.RevokedAt == null,
                cancellationToken));
    }

    [Fact]
    public async Task RegenerateRecoveryCodesAsync_WhenManagementProofIsValid_RequiresSameBearerAndRevokesAllSessions()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        var enrolled = await EnrollAndSignInAsync(
            client,
            email,
            clock,
            cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            enrolled.Tokens.AccessToken);
        clock.Advance(TimeSpan.FromSeconds(30));

        // Act
        using var reauthentication = await client.PostAsJsonAsync(
            "/api/v1/members/current/two-factor/reauthentications",
            new
            {
                purpose = "regenerateRecoveryCodes",
                code = TwoFactorTestData.CreateCurrentCode(
                    enrolled.ManualKey,
                    clock)
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            reauthentication.StatusCode);
        var grant = await reauthentication.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var flow = grant.GetProperty("flow").GetString();
        Assert.NotNull(flow);
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/two-factor/recovery-codes/regenerations",
            new
            {
                flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            denied.StatusCode);
        using var wrongOperation = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Conflict,
            wrongOperation.StatusCode);
        using var regenerated = await client.PostAsJsonAsync(
            "/api/v1/auth/two-factor/recovery-codes/regenerations",
            new
            {
                flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            regenerated.StatusCode);
        Assert.False(regenerated.Headers.Contains("Set-Cookie"));
        var codes = await regenerated.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesResponse>(cancellationToken);
        Assert.NotNull(codes);
        Assert.Equal(
            10,
            codes.RecoveryCodes.Count());
        Assert.DoesNotContain(
            codes.RecoveryCodes,
            code => enrolled.Codes.RecoveryCodes.Contains(code));
        using var revoked = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            revoked.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.True(await database.AuthenticationSessions.AllAsync(
            session => session.RevokedAt != null,
            cancellationToken));
        Assert.Equal(
            1,
            await database.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.Kind == AuthenticationEmailKind.TwoFactorRecoveryCodesRegenerated,
                cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginAsync_WhenAdministratorHasNoFactor_RequiresConfirmedEnrollmentBeforeAnySession(bool rememberMe)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();

        // Act
        using var login = await PostAsync(
            client,
            "/api/v1/auth/sessions",
            new
            {
                email,
                password = Password,
                rememberMe
            },
            cancellationToken);
        var firstFactor = await ReadChallengeAsync(
            login,
            cancellationToken);
        using var setup = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow = firstFactor.Flow
            },
            cancellationToken);
        var material = await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            "no-store",
            login.Headers.CacheControl?.ToString());
        Assert.Equal(
            TwoFactorRequiredAction.Enroll,
            firstFactor.RequiredAction);
        Assert.Equal(
            clock.GetUtcNow().UtcDateTime.AddMinutes(5),
            firstFactor.ExpiresAt);
        Assert.Equal(
            HttpStatusCode.OK,
            setup.StatusCode);
        Assert.NotNull(material);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.False(await database.AuthenticationAccessTokens.AnyAsync(cancellationToken));

        using var confirmation = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup/confirmations",
            new
            {
                flow = firstFactor.Flow,
                code = TwoFactorTestData.CreateCurrentCode(material.ManualKey, clock)
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            confirmation.StatusCode);
        var codes = await confirmation.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesResponse>(cancellationToken);
        Assert.NotNull(codes);
        Assert.Equal(
            10,
            codes.RecoveryCodes.Count());
        Assert.False(confirmation.Headers.Contains("Set-Cookie"));
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));

        using var completed = await PostAsync(
            client,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = firstFactor.Flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            completed.StatusCode);
        var tokens = await completed.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken);
        Assert.NotNull(tokens);
        Assert.Equal(
            900,
            tokens.ExpiresIn);
        Assert.Equal(
            "Bearer",
            tokens.TokenType);
        Assert.Contains(
            completed.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("MonKado.Refresh=", StringComparison.Ordinal));
        var session = await database.AuthenticationSessions.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(session.TwoFactorCredentialId);
        Assert.NotNull(session.TwoFactorVerifiedAt);
        Assert.Equal(
            rememberMe,
            session.IsPersistent);
        Assert.Equal(
            rememberMe ? clock.GetUtcNow().UtcDateTime.AddDays(30) : clock.GetUtcNow().UtcDateTime.AddHours(8),
            session.ExpiresAt);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.AccessToken);
        using var current = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
        using var status = await client.GetAsync(
            "/api/v1/members/current/two-factor",
            cancellationToken);
        var statusBody = await status.Content.ReadFromJsonAsync<TwoFactorStatusResponse>(cancellationToken);
        Assert.NotNull(statusBody);
        Assert.True(statusBody.IsEnabled);
        Assert.Equal(
            10,
            statusBody.RemainingRecoveryCodes);
    }

    [Fact]
    public async Task ConfirmSetupAsync_WhenFiveCodesFailAcrossFlows_LocksTheAccountWithoutIssuingTokens()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var email = await CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = factory.CreateClient();
        using var login = await PostAsync(
            client,
            "/api/v1/auth/sessions",
            new
            {
                email,
                password = Password
            },
            cancellationToken);
        var challenge = await ReadChallengeAsync(
            login,
            cancellationToken);
        using var setup = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);
        var material = await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(cancellationToken);
        Assert.NotNull(material);

        // Act
        for (var failure = 0; failure < 5; failure++)
        {
            using var rejected = await PostAsync(
                client,
                "/api/v1/auth/two-factor/setup/confirmations",
                new
                {
                    flow = challenge.Flow,
                    code = TwoFactorTestData.CreateIncorrectCode(material.ManualKey, clock)
                },
                cancellationToken);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                rejected.StatusCode);
        }
        using var secondLogin = await PostAsync(
            client,
            "/api/v1/auth/sessions",
            new
            {
                email,
                password = Password
            },
            cancellationToken);
        var secondFlow = await ReadChallengeAsync(
            secondLogin,
            cancellationToken);
        using var secondSetup = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow = secondFlow.Flow
            },
            cancellationToken);
        var secondMaterial = await secondSetup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(cancellationToken);
        Assert.NotNull(secondMaterial);
        using var limited = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup/confirmations",
            new
            {
                flow = secondFlow.Flow,
                code = TwoFactorTestData.CreateCurrentCode(secondMaterial.ManualKey, clock)
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limited.StatusCode);
        Assert.False(limited.Headers.Contains("Set-Cookie"));
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var factor = await database.MemberTwoFactors.SingleAsync(cancellationToken);
        Assert.Equal(
            5,
            factor.FailedAttempts);
        Assert.Equal(
            clock.GetUtcNow().UtcDateTime.AddMinutes(15),
            factor.LockedUntil);
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.False(await database.AuthenticationAccessTokens.AnyAsync(cancellationToken));
    }

    private static async Task<(AccessTokenResponse Tokens, string ManualKey, TwoFactorRecoveryCodesResponse Codes)> EnrollAndSignInAsync(
        HttpClient client,
        string email,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var challenge = await StartSignInAsync(
            client,
            email,
            cancellationToken);
        var enrollment = await ConfirmEnrollmentAsync(
            client,
            challenge.Flow,
            clock,
            cancellationToken);
        using var response = await PostAsync(
            client,
            "/api/v1/auth/two-factor/completions",
            new
            {
                flow = challenge.Flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken);
        Assert.NotNull(tokens);

        return (tokens, enrollment.ManualKey, enrollment.Codes);
    }

    private static async Task<(string ManualKey, TwoFactorRecoveryCodesResponse Codes)> ConfirmEnrollmentAsync(
        HttpClient client,
        string flow,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        using var setup = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup",
            new
            {
                flow
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            setup.StatusCode);
        var material = await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(cancellationToken);
        Assert.NotNull(material);
        using var confirmation = await PostAsync(
            client,
            "/api/v1/auth/two-factor/setup/confirmations",
            new
            {
                flow,
                code = TwoFactorTestData.CreateCurrentCode(
                    material.ManualKey,
                    clock)
            },
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            confirmation.StatusCode);
        var codes = await confirmation.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesResponse>(cancellationToken);
        Assert.NotNull(codes);

        return (material.ManualKey, codes);
    }

    private static async Task<TwoFactorChallengeResponse> StartSignInAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken)
    {
        using var response = await PostAsync(
            client,
            "/api/v1/auth/sessions",
            new
            {
                email,
                password = Password
            },
            cancellationToken);

        return await ReadChallengeAsync(
            response,
            cancellationToken);
    }

    private static async Task<string> CreateAdministratorAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        var id = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = await manager.FindByIdAsync(id.ToString("D")) ?? throw new InvalidOperationException("Missing test administrator.");
        Assert.True((await manager.AddPasswordAsync(
            user,
            Password)).Succeeded);

        return user.Email ?? throw new InvalidOperationException("Missing test email.");
    }

    private static async Task<TwoFactorChallengeResponse> ReadChallengeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(
            3,
            json.EnumerateObject().Count());

        return new TwoFactorChallengeResponse
        {
            Flow = json.GetProperty("flow").GetString() ?? throw new InvalidOperationException("Missing proof."),
            RequiredAction = Enum.Parse<TwoFactorRequiredAction>(json.GetProperty("requiredAction").GetString()!, true),
            ExpiresAt = json.GetProperty("expiresAt").GetDateTime()
        };
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var csrfResponse = await client.GetAsync(
            "/security/csrf-token",
            cancellationToken);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Missing CSRF response.");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrf.Token);

        return await client.SendAsync(
            request,
            cancellationToken);
    }
}
