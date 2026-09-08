using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistModerationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task UpdateAsync_WhenSuspendingAndReactivating_RecordsEachRealDecisionOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var administratorId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            ownerId,
            TestContext.Current.CancellationToken);
        var route = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var initial = await administrator.GetAsync(
            route,
            cancellationToken);
        var initialTag = Assert.IsType<string>(initial.Headers.ETag?.Tag);

        // Act
        using var suspended = await UpdateAsync(
            administrator,
            route,
            initialTag,
            true,
            "  Private reason  ",
            cancellationToken);
        var suspendedTag = Assert.IsType<string>(suspended.Headers.ETag?.Tag);
        var suspension = await suspended.Content.ReadFromJsonAsync<WishlistModerationResponse>(cancellationToken);
        using var repeated = await UpdateAsync(
            administrator,
            route,
            suspendedTag,
            true,
            "Private reason",
            cancellationToken);
        using var ownerView = await owner.GetAsync(
            $"/api/v1/wishlists/{wishlistId}",
            cancellationToken);
        var ownerState = await ownerView.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var reactivated = await UpdateAsync(
            administrator,
            route,
            suspendedTag,
            false,
            null,
            cancellationToken);
        var reactivation = await reactivated.Content.ReadFromJsonAsync<WishlistModerationResponse>(cancellationToken);
        using var history = await administrator.GetAsync(
            $"{route}/events",
            cancellationToken);
        var events = await history.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var outbox = await context.WishlistModerationEmails
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            suspended.StatusCode);
        Assert.NotEqual(
            initialTag,
            suspendedTag);
        Assert.True(suspended.Headers.CacheControl?.NoStore);
        Assert.NotNull(suspension);
        Assert.True(suspension.IsSuspended);
        Assert.Equal(
            "Private reason",
            suspension.SuspensionReason);
        Assert.NotNull(suspension.SuspendedAt);
        Assert.Equal(
            suspendedTag,
            repeated.Headers.ETag?.Tag);
        Assert.Equal(
            HttpStatusCode.OK,
            ownerView.StatusCode);
        Assert.True(ownerState
                .GetProperty("isSuspended")
                .GetBoolean());
        Assert.Equal(
            "Private reason",
            ownerState
                .GetProperty("suspensionReason")
                .GetString());
        Assert.Equal(
            HttpStatusCode.OK,
            reactivated.StatusCode);
        Assert.NotNull(reactivation);
        Assert.False(reactivation.IsSuspended);
        Assert.Null(reactivation.SuspensionReason);
        Assert.Null(reactivation.SuspendedAt);
        Assert.Equal(
            2,
            events
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            "reactivated",
            events.GetProperty("items")[0]
                .GetProperty("action")
                .GetString());
        Assert.Equal(
            2,
            outbox.Length);
        Assert.All(
            outbox,
            message => Assert.Null(message.ProcessedAt));
    }

    [Fact]
    public async Task GetAsync_WhenRoleIsRevoked_RejectsTheSameUnexpiredJwt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            memberId,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            TestContext.Current.CancellationToken);
        var route = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var before = await client.GetAsync(
            route,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.UserRoles
            .Where(assignment => assignment.UserId == memberId)
            .ExecuteDeleteAsync(cancellationToken);

        // Act
        using var after = await client.GetAsync(
            route,
            cancellationToken);
        var error = await after.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            before.StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            after.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistModerationForbidden,
            error.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAsync_WhenOwnerAttemptsModeration_ReturnsForbidden(bool isSuspended)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            memberId,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            TestContext.Current.CancellationToken);
        using var before = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}",
            cancellationToken);
        var entityTag = Assert.IsType<string>(before.Headers.ETag?.Tag);

        // Act
        using var response = await UpdateAsync(
            client,
            $"/api/v1/admin/wishlists/{wishlistId}/moderation",
            entityTag,
            isSuspended,
            isSuspended ? "Owner cannot decide moderation" : null,
            cancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
        using var after = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}",
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistModerationForbidden,
            error.ErrorCode);
        Assert.Equal(
            entityTag,
            after.Headers.ETag?.Tag);
        Assert.False(await context.WishlistModerationEvents.AnyAsync(cancellationToken));
        Assert.False(await context.WishlistModerationEmails.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task UpdateAsync_WhenOwnerAttemptsToLiftSuspensionThroughEitherRoute_PreservesAdministratorDecision()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var administratorId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            ownerId,
            TestContext.Current.CancellationToken);
        var moderationRoute = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        var ownerRoute = $"/api/v1/wishlists/{wishlistId}";
        using var initial = await administrator.GetAsync(
            moderationRoute,
            cancellationToken);
        using var suspension = await UpdateAsync(
            administrator,
            moderationRoute,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            true,
            "Administrator decision",
            cancellationToken);
        var suspendedTag = Assert.IsType<string>(suspension.Headers.ETag?.Tag);
        using var ordinaryUpdate = new HttpRequestMessage(
            HttpMethod.Put,
            ownerRoute)
        {
            Content = JsonContent.Create(new { name = "Modified wishlist", occasion = "other", isSuspended = false, suspensionReason = (string?)null, suspendedAt = (DateTime?)null })
        };
        ordinaryUpdate.Headers.TryAddWithoutValidation(
            "If-Match",
            suspendedTag);

        // Act
        using var moderationResponse = await UpdateAsync(
            owner,
            moderationRoute,
            suspendedTag,
            false,
            null,
            cancellationToken);
        using var ordinaryResponse = await owner.SendAsync(
            ordinaryUpdate,
            cancellationToken);
        using var current = await owner.GetAsync(
            ownerRoute,
            cancellationToken);
        var currentState = await current.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var historyResponse = await administrator.GetAsync(
            $"{moderationRoute}/events",
            cancellationToken);
        var history = await historyResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            moderationResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            ordinaryResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
        Assert.Equal(
            suspendedTag,
            current.Headers.ETag?.Tag);
        Assert.True(currentState
                .GetProperty("isSuspended")
                .GetBoolean());
        Assert.Equal(
            "Administrator decision",
            currentState
                .GetProperty("suspensionReason")
                .GetString());
        Assert.Equal(
            1,
            history
                .GetProperty("totalCount")
                .GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetSharedAsync_WhenModerated_AlsoFencesSharedMutationLocks(bool isSuspended)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var administratorId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var shareService = scope.ServiceProvider.GetRequiredService<IWishlistShareService>();
        var shareLink = await shareService.CreateAsync(
            Guid.CreateVersion7(),
            ownerId,
            wishlistId,
            cancellationToken);
        Assert.NotNull(shareLink);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        var route = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var initial = await administrator.GetAsync(
            route,
            cancellationToken);
        using var decision = await UpdateAsync(
            administrator,
            route,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            isSuspended,
            isSuspended ? "Private reason" : null,
            cancellationToken);
        using var visitor = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/shared-wishlists/{shareLink.Id}");
        request.Headers.TryAddWithoutValidation(
            "X-MonKado-Share-Token",
            shareLink.Secret);

        // Act
        using var response = await visitor.SendAsync(
            request,
            cancellationToken);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var repository = scope.ServiceProvider.GetRequiredService<IWishlistShareLinkRepository>();
        var lockedLink = await repository.LockActiveAsync(
            shareLink.Id,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            decision.StatusCode);
        Assert.Equal(
            isSuspended ? HttpStatusCode.NotFound : HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            isSuspended,
            lockedLink is null);
    }

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("DELETE", "")]
    [InlineData("POST", "/wishes")]
    [InlineData("PATCH", "/wishes")]
    [InlineData("PUT", "/wishes/{wishId}")]
    [InlineData("DELETE", "/wishes/{wishId}")]
    [InlineData("DELETE", "/wishes/{wishId}/image")]
    [InlineData("PUT", "/wishes/{wishId}/image")]
    [InlineData("GET", "/share-link")]
    [InlineData("POST", "/share-link")]
    [InlineData("PUT", "/share-link")]
    [InlineData("DELETE", "/share-link")]
    [InlineData("POST", "/wish-import-previews")]
    public async Task OwnerMutation_WhenSuspended_ReturnsConflictBeforeSideEffects(
        string method,
        string suffix)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var administratorId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        var moderationRoute = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var initial = await administrator.GetAsync(
            moderationRoute,
            cancellationToken);
        using var decision = await UpdateAsync(
            administrator,
            moderationRoute,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            true,
            "Private reason",
            cancellationToken);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            ownerId,
            TestContext.Current.CancellationToken);
        var route = $"/api/v1/wishlists/{wishlistId}" + suffix.Replace(
            "{wishId}",
            Guid
                .CreateVersion7()
                .ToString(),
            StringComparison.Ordinal);
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            route);
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            decision.Headers.ETag?.Tag);

        if (method == "PUT" && suffix.EndsWith(
            "/image",
            StringComparison.Ordinal))
        {
            var content = new MultipartFormDataContent();
            content.Add(
                new ByteArrayContent([1]),
                "image",
                "test.png");
            request.Content = content;
        }
        else
            if (method is "POST" or "PUT" or "PATCH")
            {
                request.Content = new StringContent(
                    "{}",
                    System.Text.Encoding.UTF8,
                    "application/json");
            }

        // Act
        using var response = await owner.SendAsync(
            request,
            cancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            decision.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistSuspended,
            error.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAsync_WhenCommitAcknowledgementIsLost_ConfirmsOnlyDurableDecision(bool isCommitted)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var interceptor = new WishlistModerationCommitInterceptor();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.ConfigureDbContext<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor)));
        var administratorId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        var route = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var initial = await administrator.GetAsync(
            route,
            cancellationToken);
        var initialTag = Assert.IsType<string>(initial.Headers.ETag?.Tag);

        if (isCommitted)
            interceptor.Arm();
        else
            interceptor.ArmBeforeCommit();

        // Act
        using var response = await UpdateAsync(
            administrator,
            route,
            initialTag,
            true,
            "Private durable reason",
            cancellationToken);
        using var current = await administrator.GetAsync(
            route,
            cancellationToken);
        var state = await current.Content.ReadFromJsonAsync<WishlistModerationResponse>(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var decisions = await context.WishlistModerationEvents
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var notifications = await context.WishlistModerationEmails
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        // Assert
        Assert.Equal(
            isCommitted ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.NotNull(state);
        Assert.Equal(
            isCommitted,
            state.IsSuspended);

        if (!isCommitted)
        {
            Assert.Equal(
                initialTag,
                current.Headers.ETag?.Tag);
            Assert.Empty(decisions);
            Assert.Empty(notifications);

            return;
        }

        Assert.Equal(
            response.Headers.ETag?.Tag,
            current.Headers.ETag?.Tag);
        Assert.NotEqual(
            initialTag,
            current.Headers.ETag?.Tag);
        var decision = Assert.Single(decisions);
        var notification = Assert.Single(notifications);
        Assert.Equal(
            wishlistId,
            decision.WishlistId);
        Assert.Equal(
            administratorId,
            decision.AdministratorId);
        Assert.Equal(
            WishlistModerationAction.Suspended,
            decision.Action);
        Assert.Equal(
            "Private durable reason",
            decision.Reason);
        Assert.Equal(
            decision.Id,
            notification.Id);
        Assert.Null(notification.LeaseId);
        Assert.Null(notification.LockedUntil);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public async Task UpdateAsync_WhenReasonChanges_PreservesSuspensionDateAndKeepsAuditAfterActorDeletion()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(DateTimeOffset.UnixEpoch.AddTicks(17)));
        var administratorId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        var route = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var initial = await administrator.GetAsync(
            route,
            cancellationToken);
        using var suspended = await UpdateAsync(
            administrator,
            route,
            Assert.IsType<string>(initial.Headers.ETag?.Tag),
            true,
            "Initial private reason",
            cancellationToken);
        var previous = await suspended.Content.ReadFromJsonAsync<WishlistModerationResponse>(cancellationToken);

        // Act
        using var amended = await UpdateAsync(
            administrator,
            route,
            Assert.IsType<string>(suspended.Headers.ETag?.Tag),
            true,
            "Amended private reason",
            cancellationToken);
        var state = await amended.Content.ReadFromJsonAsync<WishlistModerationResponse>(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(user => user.Id == administratorId)
            .ExecuteDeleteAsync(cancellationToken);
        var decisions = await context.WishlistModerationEvents
            .AsNoTracking()
            .OrderBy(decision => decision.Sequence)
            .ToArrayAsync(cancellationToken);
        var notificationCount = await context.WishlistModerationEmails.CountAsync(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            amended.StatusCode);
        Assert.NotNull(previous);
        Assert.NotNull(state);
        Assert.True(state.IsSuspended);
        Assert.Equal(
            DateTime.UnixEpoch.AddTicks(10),
            previous.SuspendedAt);
        Assert.Equal(
            previous.SuspendedAt,
            state.SuspendedAt);
        Assert.Equal(
            "Amended private reason",
            state.SuspensionReason);
        Assert.NotEqual(
            suspended.Headers.ETag?.Tag,
            amended.Headers.ETag?.Tag);
        Assert.Equal(
            2,
            decisions.Length);
        Assert.Equal(
            2,
            notificationCount);
        Assert.Equal(
            WishlistModerationAction.Suspended,
            decisions[0].Action);
        Assert.Equal(
            WishlistModerationAction.ReasonUpdated,
            decisions[1].Action);
        Assert.All(
            decisions,
            decision => Assert.Null(decision.AdministratorId));
    }

    [Fact]
    public async Task UpdateAsync_WhenSuspensionWinsAfterOwnerAuthorization_RejectsOwnerWriteUnderLock()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var ownerReachedGuard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOwner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.Replace(ServiceDescriptor.Scoped<IWishlistMutationGuard>(provider => new CoordinatedWishlistMutationGuard(
                        new WishlistMutationGuard(provider.GetRequiredService<MonKadoDbContext>()),
                        async token =>
                        {
                            ownerReachedGuard.TrySetResult();
                            await releaseOwner.Task.WaitAsync(token);
                        }))));
        var administratorId = await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            ownerId,
            TestContext.Current.CancellationToken);
        var moderationRoute = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var initial = await administrator.GetAsync(
            moderationRoute,
            cancellationToken);
        var entityTag = Assert.IsType<string>(initial.Headers.ETag?.Tag);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}")
        {
            Content = JsonContent.Create(new { name = "Owner change must not persist", occasion = "other" })
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        // Act
        var ownerResponseTask = owner.SendAsync(
            request,
            cancellationToken);
        HttpResponseMessage decision;
        try
        {
            await ownerReachedGuard.Task.WaitAsync(cancellationToken);
            decision = await UpdateAsync(
                administrator,
                moderationRoute,
                entityTag,
                true,
                "Private reason",
                cancellationToken);
        }
        finally
        {
            releaseOwner.TrySetResult();
        }

        using var decisionResponse = decision;
        using var ownerResponse = await ownerResponseTask;
        var error = await ownerResponse.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
        using var current = await owner.GetAsync(
            $"/api/v1/wishlists/{wishlistId}",
            cancellationToken);
        var state = await current.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            decisionResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            ownerResponse.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistSuspended,
            error.ErrorCode);
        Assert.True(state
                .GetProperty("isSuspended")
                .GetBoolean());
        Assert.Equal(
            "Private wishlist",
            state
                .GetProperty("name")
                .GetString());
    }

    [Theory]
    [InlineData("noTransaction", typeof(InvalidOperationException))]
    [InlineData("missingMember", typeof(InvalidAuthenticationSessionException))]
    [InlineData("missingWishlist", typeof(WishlistNotFoundException))]
    [InlineData("otherOwner", typeof(WishlistNotFoundException))]
    [InlineData("suspended", typeof(WishlistSuspendedException))]
    public async Task LockAsync_WhenOwnerMutationCannotProceed_RejectsAtDatabaseBoundary(
        string scenario,
        Type expectedException)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var ownerId = await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
        var wishlistId = await CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        var callerId = ownerId;

        if (scenario == "otherOwner")
            callerId = await CreateMemberAsync(
                factory,
                RoleNames.Member,
                cancellationToken);

        if (scenario == "missingMember")
            callerId = Guid.CreateVersion7();

        if (scenario == "missingWishlist")
            wishlistId = Guid.CreateVersion7();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        if (scenario == "suspended")
        {
            var wishlist = await context.Wishlists.SingleAsync(
                value => value.Id == wishlistId,
                cancellationToken);
            wishlist.Moderate(
                true,
                "Private reason",
                DateTime.UnixEpoch);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using var transaction = scenario == "noTransaction" ? null : await context.Database.BeginTransactionAsync(cancellationToken);
        var guard = scope.ServiceProvider.GetRequiredService<IWishlistMutationGuard>();

        // Act
        var exception = await Record.ExceptionAsync(() => guard.LockAsync(
                callerId,
                wishlistId,
                cancellationToken));

        // Assert
        Assert.IsType(
            expectedException,
            exception);
    }

    [Fact]
    public async Task GetAccessAsync_WhenMemberNoLongerExists_ReturnsMissingMember()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdministratorAccessService>();

        // Act
        var access = await service.GetAccessAsync(
            Guid.CreateVersion7(),
            cancellationToken);

        // Assert
        Assert.Equal(
            Application.Models.AdministratorAccess.MemberNotFound,
            access);
    }

    [Fact]
    public async Task GetAccessAsync_WhenPostgreSqlIsUnavailable_ReturnsDependencyFailure()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new PostgreSqlApiFactory("Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Timeout=1;Pooling=false;SSL Mode=Disable");
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdministratorAccessService>();

        // Act
        var action = () => service.GetAccessAsync(
            Guid.CreateVersion7(),
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LockMemberAsync_WhenMemberWasDeleted_StopsParticipantMutation(bool isReservation)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var memberId = Guid.CreateVersion7();

        // Act
        var action = isReservation ? () => scope.ServiceProvider
            .GetRequiredService<IGiftReservationTransactionFactory>()
            .LockMemberAsync(
            memberId,
            cancellationToken) : (Func<Task>)(() => scope.ServiceProvider
            .GetRequiredService<IWishlistParticipantTransactionFactory>()
            .LockMemberAsync(
            memberId,
            cancellationToken));

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
    }

    private static async Task<Guid> CreateMemberAsync(
        PostgreSqlApiFactory factory,
        string role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var id = Guid.CreateVersion7();
        var email = $"{id:N}@example.test";
        var member = new MonKadoUser
        {
            Id = id,
            Email = email,
            UserName = email,
            DisplayName = "Member",
            EmailConfirmed = true
        };
        Assert.True((await manager.CreateAsync(member)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(
                member,
                role)).Succeeded);

        return id;
    }

    private static async Task<Guid> CreateWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wishlist = new Wishlist(
            Guid.CreateVersion7(),
            ownerId,
            "Private wishlist",
            "PRIVATE WISHLIST",
            WishlistOccasion.Other,
            null,
            null);
        context.Wishlists.Add(wishlist);
        await context.SaveChangesAsync(cancellationToken);

        return wishlist.Id;
    }



    private static async Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        string route,
        string entityTag,
        bool isSuspended,
        string? reason,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            route)
        {
            Content = JsonContent.Create(new { isSuspended, reason })
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await client.SendAsync(
            request,
            cancellationToken);
    }
}
