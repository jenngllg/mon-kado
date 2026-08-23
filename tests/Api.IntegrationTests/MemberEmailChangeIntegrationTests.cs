using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class MemberEmailChangeIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string CurrentPassword = "a long secure password";
    private static readonly DateTimeOffset _referenceTime = DateTimeOffset.UtcNow;

    [Fact]
    public async Task UpdateEmailAsync_WhenRequestIsValid_PersistsRequestAndBothRecipientSnapshots()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);

        // Act
        using var response = await RequestEmailChangeAsync(
            client,
            " new@example.fr ",
            CurrentPassword,
            entityTag);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var messages = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.Kind)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.Equal(
            member.Id,
            request.UserId);
        Assert.Equal(
            "old@example.fr",
            request.CurrentEmail);
        Assert.Equal(
            "new@example.fr",
            request.NewEmail);
        Assert.Equal(
            "NEW@EXAMPLE.FR",
            request.NormalizedNewEmail);
        AssertTimestampClose(
            _referenceTime.UtcDateTime,
            request.CreatedAt,
            TimeSpan.FromMilliseconds(1));
        AssertTimestampClose(
            _referenceTime.UtcDateTime.AddHours(24),
            request.ExpiresAt,
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(
            2,
            messages.Length);
        Assert.Contains(
            messages,
            message =>
                message.Kind == AuthenticationEmailKind.EmailChangeConfirmation &&
                message.RecipientEmail == "new@example.fr" &&
                message.MemberEmailChangeRequestId == request.Id);
        Assert.Contains(
            messages,
            message =>
                message.Kind == AuthenticationEmailKind.EmailChangeSecurityNotification &&
                message.RecipientEmail == "old@example.fr" &&
                message.MemberEmailChangeRequestId == request.Id);
        Assert.Equal(
            "old@example.fr",
            storedMember.Email);
        Assert.Equal(
            "old@example.fr",
            storedMember.UserName);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenTokenIsValid_ChangesIdentityAndRevokesAllSessions()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var authorizedClient = CreateAuthorizedClient(
            factory,
            member.Id);
        var initialEntityTag = await GetCurrentEntityTagAsync(authorizedClient);
        using var requestResponse = await RequestEmailChangeAsync(
            authorizedClient,
            "new@example.fr",
            CurrentPassword,
            initialEntityTag);
        Assert.Equal(
            HttpStatusCode.Accepted,
            requestResponse.StatusCode);
        await CreateSessionsAsync(
            factory,
            member.Id);
        var confirmation = await CreateConfirmationAsync(factory);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        using var anonymousClient = factory.CreateClient();

        // Act
        using var response = await ConfirmEmailChangeAsync(
            anonymousClient,
            confirmation.RequestId,
            confirmation.Token);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var storedRequest = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);

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
            "new@example.fr",
            storedMember.Email);
        Assert.Equal(
            "NEW@EXAMPLE.FR",
            storedMember.NormalizedEmail);
        Assert.Equal(
            "new@example.fr",
            storedMember.UserName);
        Assert.Equal(
            "NEW@EXAMPLE.FR",
            storedMember.NormalizedUserName);
        Assert.True(storedMember.EmailConfirmed);
        AssertTimestampClose(
            timeProvider.GetUtcNow().UtcDateTime,
            storedRequest.ConfirmedAt ?? DateTime.MinValue,
            TimeSpan.FromMilliseconds(1));
        Assert.All(
            sessions,
            session => AssertTimestampClose(
                timeProvider.GetUtcNow().UtcDateTime,
                session.RevokedAt ?? DateTime.MinValue,
                TimeSpan.FromMilliseconds(1)));
        Assert.NotEqual(
            member.Version,
            storedMember.Version);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        Assert.Null(await userManager.FindByEmailAsync("old@example.fr"));
        Assert.NotNull(await userManager.FindByEmailAsync("new@example.fr"));
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenTokenIsReused_ReturnsGenericBadRequest()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var authorizedClient = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(authorizedClient);
        using var requestResponse = await RequestEmailChangeAsync(
            authorizedClient,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        var confirmation = await CreateConfirmationAsync(factory);
        using var anonymousClient = factory.CreateClient();
        using var firstResponse = await ConfirmEmailChangeAsync(
            anonymousClient,
            confirmation.RequestId,
            confirmation.Token);

        // Act
        using var secondResponse = await ConfirmEmailChangeAsync(
            anonymousClient,
            confirmation.RequestId,
            confirmation.Token);
        var error = await secondResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            secondResponse.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.MemberEmailChangeInvalid,
            error.ErrorCode);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenRequestWasReplaced_RejectsOldToken()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var firstRequestResponse = await RequestEmailChangeAsync(
            client,
            "first@example.fr",
            CurrentPassword,
            entityTag);
        var firstConfirmation = await CreateConfirmationAsync(factory);
        using var secondRequestResponse = await RequestEmailChangeAsync(
            client,
            "second@example.fr",
            CurrentPassword,
            entityTag);

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            firstConfirmation.RequestId,
            firstConfirmation.Token);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var requests = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .OrderBy(request => request.CreatedAt)
            .ThenBy(request => request.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.Equal(
            2,
            requests.Length);
        Assert.Single(
            requests,
            request => request.RevokedAt is not null);
        var activeRequest = Assert.Single(
            requests,
            request => request.RevokedAt is null);
        Assert.Null(activeRequest.ConfirmedAt);
        Assert.Equal(
            2,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.ProcessedAt == null,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenRequestExpires_ReturnsGenericBadRequest()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        var confirmation = await CreateConfirmationAsync(factory);
        timeProvider.Advance(TimeSpan.FromHours(24));

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            confirmation.RequestId,
            confirmation.Token);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenPasswordOrVersionIsInvalid_DoesNotCreateRequest()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);

        // Act
        using var passwordResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            "wrong-password",
            entityTag);
        using var staleResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            "\"00000000\"");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            passwordResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            staleResponse.StatusCode);
        Assert.Empty(await context.MemberEmailChangeRequests
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenCurrentEmailIsRequested_ReturnsAcceptedWithoutPasswordVerificationOrWrite()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);

        // Act
        using var response = await RequestEmailChangeAsync(
            client,
            "OLD@example.fr",
            "wrong-password",
            entityTag);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.Empty(await context.MemberEmailChangeRequests
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenSameActiveChangeIsRequestedTwice_KeepsOriginalRequest()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var firstResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        await using var setupScope = factory.Services.CreateAsyncScope();
        var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var originalRequestId = await setupContext.MemberEmailChangeRequests
            .AsNoTracking()
            .Select(request => request.Id)
            .SingleAsync(TestContext.Current.CancellationToken);

        // Act
        using var secondResponse = await RequestEmailChangeAsync(
            client,
            "NEW@example.fr",
            CurrentPassword,
            entityTag);
        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertionContext = assertionScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var requests = await assertionContext.MemberEmailChangeRequests
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var messages = await assertionContext.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            secondResponse.StatusCode);
        var request = Assert.Single(requests);
        Assert.Equal(
            originalRequestId,
            request.Id);
        Assert.Null(request.RevokedAt);
        Assert.Equal(
            2,
            messages.Length);
        Assert.All(
            messages,
            message => Assert.Null(message.ProcessedAt));
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenSamePreviousRequestIsExpired_ReplacesRequest()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var firstResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        timeProvider.Advance(TimeSpan.FromHours(24));

        // Act
        using var secondResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var requests = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .OrderBy(request => request.CreatedAt)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var messages = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            secondResponse.StatusCode);
        Assert.Equal(
            2,
            requests.Length);
        Assert.Single(
            requests,
            request => request.RevokedAt is not null);
        Assert.Single(
            requests,
            request => request.RevokedAt is null);
        Assert.Equal(
            4,
            messages.Length);
        Assert.Equal(
            2,
            messages.Count(message => message.ProcessedAt is null));
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenPasswordHashRequiresRehash_DoesNotChangeMemberVersion()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var storedMember = await context.Users
                .SingleAsync(
                    candidate => candidate.Id == member.Id,
                    TestContext.Current.CancellationToken);
            var legacyHasher = new PasswordHasher<MonKadoUser>(
                Microsoft.Extensions.Options.Options.Create(
                    new PasswordHasherOptions { IterationCount = 10_000 }));
            var legacyHash = legacyHasher.HashPassword(
                storedMember,
                CurrentPassword);
            storedMember.PasswordHash = legacyHash;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var currentHasher = setupScope.ServiceProvider
                .GetRequiredService<IPasswordHasher<MonKadoUser>>();
            Assert.Equal(
                PasswordVerificationResult.SuccessRehashNeeded,
                currentHasher.VerifyHashedPassword(
                    storedMember,
                    legacyHash,
                    CurrentPassword));
        }

        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);

        // Act
        using var response = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        var currentEntityTag = await GetCurrentEntityTagAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.Equal(
            entityTag,
            currentEntityTag);
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenMemberHasNoPassword_ReturnsForbidden()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberWithoutPasswordAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);

        // Act
        using var response = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.MemberEmailChangeRequests
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenMemberDoesNotExist_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            "\"00000001\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task UpdateEmailAsync_WhenEmailIsAlreadyUsed_ReturnsConflict()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        await CreateMemberAsync(
            factory,
            "used@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);

        // Act
        using var response = await RequestEmailChangeAsync(
            client,
            "used@example.fr",
            CurrentPassword,
            entityTag);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
    }

    [Fact]
    public async Task EmailChangeAsync_WhenInnerNormalizerReturnsNull_UsesInvariantNormalizationThroughConfirmation()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            configureServices: services =>
            {
                services.RemoveAll<ILookupNormalizer>();
                services.AddSingleton<ConditionalNullLookupNormalizer>();
                services.AddSingleton<ILookupNormalizer>(provider =>
                    new InvariantFallbackLookupNormalizer(
                        provider.GetRequiredService<ConditionalNullLookupNormalizer>()));
            });
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);

        // Act
        using var response = await RequestEmailChangeAsync(
            client,
            ConditionalNullLookupNormalizer.NullEmail,
            CurrentPassword,
            entityTag);
        var confirmation = await CreateConfirmationAsync(factory);
        using var anonymousClient = factory.CreateClient();
        using var confirmationResponse = await ConfirmEmailChangeAsync(
            anonymousClient,
            confirmation.RequestId,
            confirmation.Token);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmationResponse.StatusCode);
        Assert.Equal(
            ConditionalNullLookupNormalizer.NullEmail.ToUpperInvariant(),
            request.NormalizedNewEmail);
        Assert.Equal(
            ConditionalNullLookupNormalizer.NullEmail,
            storedMember.Email);
        Assert.Equal(
            ConditionalNullLookupNormalizer.NullEmail.ToUpperInvariant(),
            storedMember.NormalizedEmail);
        Assert.Equal(
            ConditionalNullLookupNormalizer.NullEmail.ToUpperInvariant(),
            storedMember.NormalizedUserName);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenEmailWasAssignedMeanwhile_ReturnsConflictWithoutChangingMember()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        var confirmation = await CreateConfirmationAsync(factory);
        await CreateMemberAsync(
            factory,
            "new@example.fr");

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            confirmation.RequestId,
            confirmation.Token);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == member.Id,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        Assert.Equal(
            "old@example.fr",
            storedMember.Email);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenTwoMembersConfirmSameEmailConcurrently_OnlyOneSucceeds()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            configureServices: services =>
            {
                services.AddSingleton<ConcurrentEmailChangeCoordinator>();
                services.RemoveAll<UserManager<MonKadoUser>>();
                services.AddScoped<UserManager<MonKadoUser>, ConcurrentMemberEmailChangeUserManager>();
            });
        var firstMember = await CreateMemberAsync(
            factory,
            "first-old@example.fr");
        var secondMember = await CreateMemberAsync(
            factory,
            "second-old@example.fr");
        using var firstClient = CreateAuthorizedClient(
            factory,
            firstMember.Id);
        using var secondClient = CreateAuthorizedClient(
            factory,
            secondMember.Id);
        var firstEntityTag = await GetCurrentEntityTagAsync(firstClient);
        var secondEntityTag = await GetCurrentEntityTagAsync(secondClient);
        using var firstRequestResponse = await RequestEmailChangeAsync(
            firstClient,
            "shared@example.fr",
            CurrentPassword,
            firstEntityTag);
        using var secondRequestResponse = await RequestEmailChangeAsync(
            secondClient,
            "shared@example.fr",
            CurrentPassword,
            secondEntityTag);
        var firstConfirmation = await CreateConfirmationAsync(
            factory,
            firstMember.Id);
        var secondConfirmation = await CreateConfirmationAsync(
            factory,
            secondMember.Id);

        // Act
        var responses = await Task.WhenAll(
            ConfirmEmailChangeAsync(
                firstClient,
                firstConfirmation.RequestId,
                firstConfirmation.Token),
            ConfirmEmailChangeAsync(
                secondClient,
                secondConfirmation.RequestId,
                secondConfirmation.Token));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var conflictResponse = responses.Single(response =>
            response.StatusCode == HttpStatusCode.Conflict);
        var error = await conflictResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMembers = await context.Users
            .AsNoTracking()
            .Where(member =>
                member.Id == firstMember.Id ||
                member.Id == secondMember.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var storedRequests = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .Where(request =>
                request.Id == firstConfirmation.RequestId ||
                request.Id == secondConfirmation.RequestId)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            responses.Count(response => response.StatusCode == HttpStatusCode.NoContent));
        Assert.Equal(
            1,
            responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.MemberEmailAlreadyUsed,
            error.ErrorCode);
        Assert.Equal(
            1,
            storedMembers.Count(member => member.NormalizedEmail == "SHARED@EXAMPLE.FR"));
        Assert.Equal(
            1,
            storedMembers.Count(member =>
                member.Email is "first-old@example.fr" or "second-old@example.fr"));
        Assert.Equal(
            1,
            storedRequests.Count(request => request.ConfirmedAt is not null));
        Assert.Equal(
            1,
            storedRequests.Count(request => request.ConfirmedAt is null));
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenTokenEncodingIsInvalid_ReturnsGenericBadRequest()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            Guid.CreateVersion7(),
            "a");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenRequestDoesNotExist_ReturnsGenericBadRequest()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = factory.CreateClient();

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            Guid.CreateVersion7(),
            AuthenticationEmailTokenEncoding.Encode("token"));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.MemberEmailChangeInvalid,
            error.ErrorCode);
    }

    [Fact]
    public async Task ConfirmEmailChangeAsync_WhenTokenIsAltered_ReturnsGenericBadRequest()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        var confirmation = await CreateConfirmationAsync(factory);
        var alteredToken = confirmation.Token[..^1] +
            (confirmation.Token[^1] == 'A'
                ? "B"
                : "A");

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            confirmation.RequestId,
            alteredToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("missing-member")]
    [InlineData("changed-current-email")]
    public async Task ConfirmEmailChangeAsync_WhenMemberStateNoLongerMatches_ReturnsGenericBadRequest(
        string scenario)
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        var confirmation = await CreateConfirmationAsync(factory);
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            if (scenario == "changed-current-email")
                await context.Users
                    .Where(user => user.Id == member.Id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            user => user.Email,
                            "changed@example.fr"),
                        TestContext.Current.CancellationToken);

            if (scenario == "missing-member")
            {
                await context.Database.OpenConnectionAsync(
                    TestContext.Current.CancellationToken);

                try
                {
                    await context.Database.ExecuteSqlRawAsync(
                        "SET session_replication_role = replica;",
                        TestContext.Current.CancellationToken);
                    await context.Users
                        .Where(user => user.Id == member.Id)
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
        }

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            confirmation.RequestId,
            confirmation.Token);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData(
        "duplicate-email@example.fr",
        HttpStatusCode.Conflict,
        ErrorCodes.MemberEmailAlreadyUsed)]
    [InlineData(
        "duplicate-user-name@example.fr",
        HttpStatusCode.Conflict,
        ErrorCodes.MemberEmailAlreadyUsed)]
    [InlineData(
        "unique-violation@example.fr",
        HttpStatusCode.Conflict,
        ErrorCodes.MemberEmailAlreadyUsed)]
    [InlineData(
        "concurrency-failure@example.fr",
        HttpStatusCode.BadRequest,
        ErrorCodes.MemberEmailChangeInvalid)]
    [InlineData(
        "concurrency-exception@example.fr",
        HttpStatusCode.BadRequest,
        ErrorCodes.MemberEmailChangeInvalid)]
    [InlineData(
        "generic-failure@example.fr",
        HttpStatusCode.InternalServerError,
        null)]
    [InlineData(
        "stamp-concurrency-failure@example.fr",
        HttpStatusCode.BadRequest,
        ErrorCodes.MemberEmailChangeInvalid)]
    [InlineData(
        "stamp-generic-failure@example.fr",
        HttpStatusCode.InternalServerError,
        null)]
    public async Task ConfirmEmailChangeAsync_WhenIdentityCannotPersist_RollsBackAndReturnsExpectedError(
        string requestedEmail,
        HttpStatusCode expectedStatusCode,
        string? expectedErrorCode)
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            configureServices: services =>
            {
                services.RemoveAll<UserManager<MonKadoUser>>();
                services.AddScoped<UserManager<MonKadoUser>, FailingMemberEmailChangeUserManager>();
            });
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            requestedEmail,
            CurrentPassword,
            entityTag);
        var confirmation = await CreateConfirmationAsync(factory);

        // Act
        using var response = await ConfirmEmailChangeAsync(
            client,
            confirmation.RequestId,
            confirmation.Token);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var storedRequest = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatusCode,
            response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            expectedErrorCode,
            error.ErrorCode);
        Assert.Equal(
            "old@example.fr",
            storedMember.Email);
        Assert.Null(storedRequest.ConfirmedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmailChangeAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable(
        bool confirmsRequest)
    {
        // Arrange
        await using var factory = new PostgreSqlApiFactory(
            "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;" +
            "Password=unavailable;Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable");
        using var client = confirmsRequest
            ? factory.CreateClient()
            : CreateAuthorizedClient(
                factory,
                Guid.CreateVersion7());

        // Act
        using var response = confirmsRequest
            ? await ConfirmEmailChangeAsync(
                client,
                Guid.CreateVersion7(),
                AuthenticationEmailTokenEncoding.Encode("token"))
            : await RequestEmailChangeAsync(
                client,
                "new@example.fr",
                CurrentPassword,
                "\"00000001\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
    }

    [Fact]
    public async Task DeleteExpiredRequestsAsync_WhenProcessedRequestReachesCleanupCutoff_DeletesByRetentionPolicy()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        await using var scope = factory.Services.CreateAsyncScope();
        var cleanup = scope.ServiceProvider
            .GetRequiredService<IExpiredMemberEmailChangeRequestCleanup>();

        // Act
        var beforeExpiration = await cleanup.DeleteExpiredRequestsAsync(
            _referenceTime.UtcDateTime.AddHours(23),
            500,
            TestContext.Current.CancellationToken);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.AuthenticationEmailOutboxMessages
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    _referenceTime.UtcDateTime),
                TestContext.Current.CancellationToken);
        var atExpiration = await cleanup.DeleteExpiredRequestsAsync(
            _referenceTime.UtcDateTime.AddHours(24),
            500,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            0,
            beforeExpiration);
        Assert.Equal(
            1,
            atExpiration);
        Assert.Empty(await context.MemberEmailChangeRequests
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteExpiredRequestsAsync_WhenExpiredRequestHasPendingEmail_KeepsItUntilEmailIsProcessed()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var cleanup = scope.ServiceProvider
            .GetRequiredService<IExpiredMemberEmailChangeRequestCleanup>();
        var cutoff = _referenceTime.UtcDateTime.AddHours(24);

        // Act
        var whilePending = await cleanup.DeleteExpiredRequestsAsync(
            cutoff,
            500,
            TestContext.Current.CancellationToken);
        await context.AuthenticationEmailOutboxMessages
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    cutoff),
                TestContext.Current.CancellationToken);
        var afterProcessing = await cleanup.DeleteExpiredRequestsAsync(
            cutoff,
            500,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            0,
            whilePending);
        Assert.Equal(
            1,
            afterProcessing);
        Assert.Empty(await context.MemberEmailChangeRequests
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteExpiredRequestsAsync_WhenRequestIsCompleted_AppliesSevenDayRetention(
        bool isConfirmed)
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .SingleAsync(TestContext.Current.CancellationToken);

        if (isConfirmed)
            request.Confirm(_referenceTime.UtcDateTime);

        if (!isConfirmed)
            request.Revoke(_referenceTime.UtcDateTime);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await context.AuthenticationEmailOutboxMessages
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    _referenceTime.UtcDateTime),
                TestContext.Current.CancellationToken);
        var cleanup = scope.ServiceProvider
            .GetRequiredService<IExpiredMemberEmailChangeRequestCleanup>();

        // Act
        var beforeRetention = await cleanup.DeleteExpiredRequestsAsync(
            _referenceTime.UtcDateTime.AddDays(7).AddMilliseconds(-1),
            500,
            TestContext.Current.CancellationToken);
        var atRetention = await cleanup.DeleteExpiredRequestsAsync(
            _referenceTime.UtcDateTime.AddDays(7),
            500,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            0,
            beforeRetention);
        Assert.Equal(
            1,
            atRetention);
        Assert.Empty(await context.MemberEmailChangeRequests
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteExpiredRequestsAsync_WhenCompletedRequestHasPendingEmail_KeepsItUntilEmailIsProcessed()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var member = await CreateMemberAsync(
            factory,
            "old@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        var entityTag = await GetCurrentEntityTagAsync(client);
        using var requestResponse = await RequestEmailChangeAsync(
            client,
            "new@example.fr",
            CurrentPassword,
            entityTag);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .SingleAsync(TestContext.Current.CancellationToken);
        request.Confirm(_referenceTime.UtcDateTime);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var cleanup = scope.ServiceProvider
            .GetRequiredService<IExpiredMemberEmailChangeRequestCleanup>();
        var cutoff = _referenceTime.UtcDateTime.AddDays(7);

        // Act
        var whilePending = await cleanup.DeleteExpiredRequestsAsync(
            cutoff,
            500,
            TestContext.Current.CancellationToken);
        await context.AuthenticationEmailOutboxMessages
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    cutoff),
                TestContext.Current.CancellationToken);
        var afterProcessing = await cleanup.DeleteExpiredRequestsAsync(
            cutoff,
            500,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            0,
            whilePending);
        Assert.Equal(
            1,
            afterProcessing);
        Assert.Empty(await context.MemberEmailChangeRequests
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync(
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider ?? new FixedTimeProvider(_referenceTime),
            configureServices: configureServices);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<MonKadoUser> CreateMemberAsync(
        PostgreSqlApiFactory factory,
        string email)
    {

        return await CreateMemberCoreAsync(
            factory,
            email,
            CurrentPassword);
    }

    private static async Task<MonKadoUser> CreateMemberWithoutPasswordAsync(
        PostgreSqlApiFactory factory,
        string email)
    {

        return await CreateMemberCoreAsync(
            factory,
            email,
            password: null);
    }

    private static async Task<MonKadoUser> CreateMemberCoreAsync(
        PostgreSqlApiFactory factory,
        string email,
        string? password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_referenceTime),
            Email = email,
            UserName = email,
            DisplayName = "Email change test",
            EmailConfirmed = true
        };
        var creationResult = password is null
            ? await userManager.CreateAsync(member)
            : await userManager.CreateAsync(
                member,
                password);
        Assert.True(
            creationResult.Succeeded,
            string.Join(
                ", ",
                creationResult.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(
            member,
            RoleNames.Member);
        Assert.True(
            roleResult.Succeeded,
            string.Join(
                ", ",
                roleResult.Errors.Select(error => error.Description)));

        return member;
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

    private static async Task<string> GetCurrentEntityTagAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        return response.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The current member ETag is missing.");
    }

    private static async Task<HttpResponseMessage> RequestEmailChangeAsync(
        HttpClient client,
        string email,
        string currentPassword,
        string entityTag)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/email")
        {
            Content = JsonContent.Create(new
            {
                email,
                currentPassword
            })
        };
        request.Headers.IfMatch.ParseAdd(entityTag);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<(Guid RequestId, string Token)> CreateConfirmationAsync(
        PostgreSqlApiFactory factory)
    {

        return await CreateConfirmationAsync(
            factory,
            memberId: null);
    }

    private static async Task<(Guid RequestId, string Token)> CreateConfirmationAsync(
        PostgreSqlApiFactory factory,
        Guid? memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(
                candidate =>
                    candidate.RevokedAt == null &&
                    (memberId == null || candidate.UserId == memberId),
                TestContext.Current.CancellationToken);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = await userManager.FindByIdAsync(request.UserId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        var purpose = MemberEmailChangeTokenPurpose.Create(
            request.Id,
            request.NormalizedNewEmail);
        var token = await userManager.GenerateUserTokenAsync(
            user,
            EmailChangeTokenProviderOptions.ProviderName,
            purpose);

        return (
            request.Id,
            AuthenticationEmailTokenEncoding.Encode(token));
    }

    private static async Task<HttpResponseMessage> ConfirmEmailChangeAsync(
        HttpClient client,
        Guid requestId,
        string token)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
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
