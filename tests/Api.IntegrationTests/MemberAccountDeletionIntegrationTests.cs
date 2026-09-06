using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class MemberAccountDeletionIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string RequestPath = "/api/v1/members/current/deletion-requests";
    [Fact]
    public async Task ConfirmAsync_WhenCommittedOutcomeCannotBeVerified_ReturnsUnavailableWithoutClaimingSuccess()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var failure = new AccountDeletionVerificationFailure();
        var interceptor = new AccountDeletionLostCommitInterceptor(failure);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(
                    interceptor,
                    failure)));
        await using var scope = factory.Services.CreateAsyncScope();
        var member = await CreateMemberAsync(
            scope.ServiceProvider,
            "unknown-outcome@example.test");
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionService>()
            .RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);
        var request = await context.MemberAccountDeletionRequests.SingleAsync(TestContext.Current.CancellationToken);
        var token = scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            member.Id,
            request.Id);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        interceptor.Armed = true;

        // Act
        using var response = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);
        failure.Unavailable = false;

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.False(await context.Users.AnyAsync(
                user => user.Id == member.Id,
                TestContext.Current.CancellationToken));
        using var retry = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            retry.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unconfirmed")]
    [InlineData("stamp")]
    public async Task RequestAsync_WhenAccountCannotConfirm_RejectsWithoutQueuingEmail(string scenario)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var member = await CreateMemberAsync(
            scope.ServiceProvider,
            "ineligible@example.test");
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        switch (scenario)
        {
            case "missing":
                await context.Users.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
                break;
            case "unconfirmed":
                await context.Users.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.EmailConfirmed,
                        false),
                    TestContext.Current.CancellationToken);
                break;
            case "stamp":
                await context.Users.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.SecurityStamp,
                        (string?)null),
                    TestContext.Current.CancellationToken);
                break;
        }

        context.ChangeTracker.Clear();
        var service = scope.ServiceProvider.GetRequiredService<IMemberAccountDeletionService>();

        // Act
        var action = () => service.RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmAsync_WhenMemberIsMissingOrTokenIsInvalid_RejectsAtServiceBoundary(bool validToken)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var memberId = Guid.CreateVersion7();
        var token = validToken ? scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            memberId,
            Guid.CreateVersion7()) : "invalid";
        var service = scope.ServiceProvider.GetRequiredService<IMemberAccountDeletionService>();

        // Act
        var action = () => service.ConfirmAsync(
            memberId,
            token,
            TestContext.Current.CancellationToken);

        // Assert
        if (validToken)
            await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        else
            await Assert.ThrowsAsync<MemberAccountDeletionInvalidException>(action);
    }

    [Fact]
    public async Task IdentityServices_WhenMemberWasDeleted_KeepTheirConcurrentDeletionGuards()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var memberId = Guid.CreateVersion7();
        var email = scope.ServiceProvider.GetRequiredService<IMemberEmailChangeService>();
        var password = scope.ServiceProvider.GetRequiredService<IMemberPasswordService>();
        var wishlists = scope.ServiceProvider.GetRequiredService<IWishlistRepository>();

        // Act
        var emailResult = await email.RequestAsync(
            memberId,
            "new@example.test",
            "password",
            1,
            TestContext.Current.CancellationToken);
        var passwordResult = await password.ChangeAsync(
            memberId,
            "old",
            "new",
            TestContext.Current.CancellationToken);
        var wishlistAccess = await wishlists.GetAccessAsync(
            memberId,
            Guid.CreateVersion7(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(emailResult);
        Assert.False(passwordResult);
        Assert.Equal(
            JennGllg.Fr.MonKado.Back.Application.Models.WishlistAccess.MemberNotFound,
            wishlistAccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmAsync_WhenAnotherRequestIsConcurrent_PreventsRecreationAndDuplicateConfirmation(bool createWishlist)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var barrier = new AccountDeletionCommitBarrier();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(barrier)));
        await using var scope = factory.Services.CreateAsyncScope();
        var member = await CreateMemberAsync(
            scope.ServiceProvider,
            "concurrent@example.test");
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionService>()
            .RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);
        var request = await context.MemberAccountDeletionRequests.SingleAsync(TestContext.Current.CancellationToken);
        var token = scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            member.Id,
            request.Id);
        using var firstClient = CreateAuthorizedClient(
            factory,
            member.Id);
        using var secondClient = CreateAuthorizedClient(
            factory,
            member.Id);
        barrier.Arm();

        // Act
        var firstTask = firstClient.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);
        await barrier.WaitUntilEnteredAsync(TestContext.Current.CancellationToken);
        Task<HttpResponseMessage> secondTask;
        try
        {
            secondTask = createWishlist ? secondClient.PostAsJsonAsync(
                "/api/v1/wishlists",
                new
                {
                    name = "Concurrent list",
                    occasion = "other"
                },
                TestContext.Current.CancellationToken) : secondClient.PostAsJsonAsync(
                RequestPath + "/confirm",
                new
                {
                    token
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            barrier.Release();
        }

        using var firstResponse = await firstTask;
        using var secondResponse = await secondTask;

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            secondResponse.StatusCode);
        Assert.Empty(await context.Users
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.Wishlists
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmAsync_WhenCommitFails_ResolvesOutcomeWithoutPartialDeletion(bool committed)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.AddDbContextPool<MonKadoDbContext>((
                    _,
                    options) => options.AddInterceptors(interceptor)));
        await using var scope = factory.Services.CreateAsyncScope();
        var member = await CreateMemberAsync(
            scope.ServiceProvider,
            "commit@example.test");
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var list = new Wishlist(
            Guid.CreateVersion7(),
            member.Id,
            "Commit list",
            "COMMIT LIST",
            WishlistOccasion.Other,
            null,
            null);
        var wish = new Wish(
            Guid.CreateVersion7(),
            list.Id,
            "Commit gift",
            null,
            null,
            null,
            1);
        wish.ReplaceImage(
            Guid.CreateVersion7(),
            new byte[32]);
        context.AddRange(
            list,
            wish);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionService>()
            .RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);
        var request = await context.MemberAccountDeletionRequests.SingleAsync(TestContext.Current.CancellationToken);
        var token = scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            member.Id,
            request.Id);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);

        if (committed)
            interceptor.Arm();
        else
            interceptor.ArmBeforeCommit();

        // Act
        using var response = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            committed ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            !committed,
            await context.Users.AnyAsync(
                user => user.Id == member.Id,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            !committed,
            await context.Wishlists.AnyAsync(
                value => value.Id == list.Id,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            committed,
            await context.GiftImageDeletionOutboxMessages.AnyAsync(
                value => value.ImageId == wish.ImageId,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            !committed,
            await context.MemberAccountDeletionRequests.AnyAsync(
                value => value.Id == request.Id,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RequestAsync_WhenQuotaIsExhausted_RejectsFourthRequestAndPreservesLatestToken()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var member = await CreateMemberAsync(
            scope.ServiceProvider,
            "quota@example.test");
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        for (var index = 0; index < 3; index++)
        {
            using var accepted = await client.PostAsync(
                RequestPath,
                null,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.Accepted,
                accepted.StatusCode);
        }

        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberAccountDeletionRequests.SingleAsync(TestContext.Current.CancellationToken);

        // Act
        using var rejected = await client.PostAsync(
            RequestPath,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);
        Assert.Equal(
            request.Id,
            (await context.MemberAccountDeletionRequests
                .AsNoTracking()
                .SingleAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Equal(
            3,
            await context.AuthenticationEmailOutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConfirmAsync_WhenMemberHasOwnedAndForeignReservations_CleansImagesAndRetainsMinimalForeignHistory()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var owner = await CreateMemberAsync(
            scope.ServiceProvider,
            "owner@example.test");
        var other = await CreateMemberAsync(
            scope.ServiceProvider,
            "other@example.test");
        var owned = new Wishlist(
            Guid.CreateVersion7(),
            owner.Id,
            "Private list",
            "PRIVATE LIST",
            WishlistOccasion.Other,
            null,
            null);
        var foreign = new Wishlist(
            Guid.CreateVersion7(),
            other.Id,
            "Other list",
            "OTHER LIST",
            WishlistOccasion.Other,
            null,
            null);
        var ownedWish = new Wish(
            Guid.CreateVersion7(),
            owned.Id,
            "Private gift",
            null,
            null,
            null,
            1,
            3);
        var foreignWish = new Wish(
            Guid.CreateVersion7(),
            foreign.Id,
            "Other gift",
            null,
            null,
            null,
            1,
            3);
        var imageId = Guid.CreateVersion7();
        ownedWish.ReplaceImage(
            imageId,
            new byte[32]);
        var ownerParticipation = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            foreign.Id,
            owner.Id);
        var otherParticipation = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            owned.Id,
            other.Id);
        var ownerReservation = new GiftReservation(
            Guid.CreateVersion7(),
            foreign.Id,
            foreignWish.Id,
            ownerParticipation.Id,
            2);
        var otherReservation = new GiftReservation(
            Guid.CreateVersion7(),
            owned.Id,
            ownedWish.Id,
            otherParticipation.Id,
            1);
        var now = DateTime.UtcNow;
        context.AddRange(
            owned,
            foreign,
            ownedWish,
            foreignWish,
            ownerParticipation,
            otherParticipation,
            ownerReservation,
            otherReservation);
        context.GiftReservationHistories.AddRange(
            new GiftReservationHistory(
                ownerReservation.Id,
                owner.Id,
                foreign.Id,
                foreign.Name,
                foreignWish.Id,
                foreignWish.Name,
                2,
                now,
                now),
            new GiftReservationHistory(
                otherReservation.Id,
                other.Id,
                owned.Id,
                owned.Name,
                ownedWish.Id,
                ownedWish.Name,
                1,
                now,
                now));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<IMemberAccountDeletionService>();
        await service.RequestAsync(
            owner.Id,
            TestContext.Current.CancellationToken);
        var request = await context.MemberAccountDeletionRequests.SingleAsync(TestContext.Current.CancellationToken);
        var token = scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            owner.Id,
            request.Id);

        // Act
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var response = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        context.ChangeTracker.Clear();
        Assert.Equal(
            foreign.Id,
            (await context.Wishlists.SingleAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Equal(
            foreignWish.Id,
            (await context.Wishes.SingleAsync(TestContext.Current.CancellationToken)).Id);
        Assert.Empty(await context.GiftReservations.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.WishlistParticipants.ToArrayAsync(TestContext.Current.CancellationToken));
        var deletion = await context.GiftImageDeletionOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            imageId,
            deletion.ImageId);
        var history = await context.GiftReservationHistories.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            other.Id,
            history.MemberId);
        Assert.Equal(
            "Deleted wishlist",
            history.WishlistName);
        Assert.Equal(
            "Deleted gift",
            history.WishName);
        Assert.Equal(
            GiftReservationHistoryStatus.Unavailable,
            history.Status);
        Assert.Equal(
            1,
            history.Quantity);
        Assert.Equal(
            now,
            history.CreatedAt,
            TimeSpan.FromMilliseconds(1));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("superseded")]
    [InlineData("email")]
    [InlineData("stamp")]
    public async Task ConfirmAsync_WhenRequestBecomesObsolete_PreservesAccount(string scenario)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        await using var scope = factory.Services.CreateAsyncScope();
        var member = await CreateMemberAsync(
            scope.ServiceProvider,
            "obsolete@example.test");
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IMemberAccountDeletionService>();
        await service.RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);
        var request = await context.MemberAccountDeletionRequests.SingleAsync(TestContext.Current.CancellationToken);
        var token = scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            member.Id,
            request.Id);
        context.ChangeTracker.Clear();
        switch (scenario)
        {
            case "expired":
                clock.Advance(TimeSpan.FromMinutes(30));
                break;
            case "superseded":
                await service.RequestAsync(
                    member.Id,
                    TestContext.Current.CancellationToken);
                break;
            case "email":
                await context.Users
                    .Where(user => user.Id == member.Id)
                    .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.Email,
                        "new@example.test"),
                    TestContext.Current.CancellationToken);
                break;
            case "stamp":
                await context.Users
                    .Where(user => user.Id == member.Id)
                    .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.SecurityStamp,
                        "new-stamp"),
                    TestContext.Current.CancellationToken);
                break;
        }

        using var client = CreateAuthorizedClient(
            factory,
            member.Id);

        // Act
        using var response = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.True(await context.Users.AnyAsync(
                user => user.Id == member.Id,
                TestContext.Current.CancellationToken));
    }

    private static async Task<MonKadoUser> CreateMemberAsync(
        IServiceProvider services,
        string email)
    {
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Deletion test"
        };
        var result = await services
            .GetRequiredService<UserManager<MonKadoUser>>()
            .CreateAsync(member);
        Assert.True(result.Succeeded);

        return member;
    }

    private static HttpClient CreateAuthorizedClient(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessToken = factory.Services
            .GetRequiredService<IAccessTokenService>()
            .Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken.Value);

        return client;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmAsync_WhenRequestMatchesAccount_DeletesAccountAndRejectsOldJwt(bool hasPassword)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            UserName = "delete@example.test",
            Email = "delete@example.test",
            EmailConfirmed = true,
            DisplayName = "Deletion test"
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var creation = hasPassword ? await userManager.CreateAsync(
            member,
            "a long secure password") : await userManager.CreateAsync(member);
        Assert.True(creation.Succeeded);
        var linked = await userManager.AddLoginAsync(
            member,
            new UserLoginInfo(
                "Google",
                "deleted-google-subject",
                "Google"));
        Assert.True(linked.Succeeded);
        var sessionContext = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var now = factory.Services
            .GetRequiredService<TimeProvider>()
            .GetUtcNow()
            .UtcDateTime;
        sessionContext.AuthenticationSessions.Add(AuthenticationSession.Create(
                Guid.CreateVersion7(),
                member.Id,
                new byte[32],
                false,
                now,
                now.AddHours(8)));
        await sessionContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var client = factory.CreateClient();
        var accessToken = factory.Services
            .GetRequiredService<IAccessTokenService>()
            .Create(member.Id);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken.Value);
        using var requested = await client.PostAsync(
            RequestPath,
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.Accepted,
            requested.StatusCode);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberAccountDeletionRequests
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(await context.Users.AnyAsync(
                user => user.Id == member.Id,
                TestContext.Current.CancellationToken));
        var token = scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            member.Id,
            request.Id);

        // Act
        using var confirmed = await client.PostAsJsonAsync(
            RequestPath + "/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);
        using var oldJwt = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            confirmed.StatusCode);
        Assert.True(confirmed.Headers.CacheControl?.NoStore);
        Assert.Contains(
            confirmed.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            oldJwt.StatusCode);
        Assert.False(await context.Users.AnyAsync(
                user => user.Id == member.Id,
                TestContext.Current.CancellationToken));
        Assert.Empty(await context.MemberAccountDeletionRequests
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.AuthenticationSessions
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await context.UserLogins
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken));
        var newMember = await CreateMemberAsync(
            scope.ServiceProvider,
            "delete@example.test");
        Assert.NotEqual(
            member.Id,
            newMember.Id);
        using var stillInvalid = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            stillInvalid.StatusCode);
    }
}
