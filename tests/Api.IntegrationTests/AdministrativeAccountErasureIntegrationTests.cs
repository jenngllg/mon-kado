using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using SkiaSharp;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class AdministrativeAccountErasureIntegrationTests(PostgreSqlContainerFixture fixture) : IAsyncLifetime
{
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    private readonly string _storagePath = Path.Combine(
        Path.GetTempPath(),
        $"monkado-admin-erasure-tests-{Guid.CreateVersion7():N}");

    public ValueTask InitializeAsync()
    {

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        var path = Path.GetFullPath(_storagePath);

        if (Path.GetDirectoryName(path) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
            throw new InvalidOperationException("Unexpected erasure test storage directory.");

        if (Directory.Exists(path))
            Directory.Delete(
                path,
                recursive: true);
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WhenTargetExists_ErasesAndAuditsWithConditionalProtectedNotification(bool confirmed)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            memberId,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var email = await context.Users
            .Where(member => member.Id == memberId)
            .Select(member => member.Email)
            .SingleAsync(cancellationToken);
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.EmailConfirmed,
                    confirmed),
                cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        using var member = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            TestContext.Current.CancellationToken);

        // Act
        using var response = await administrator.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "  SUPPORT-808  ",
                confirmedMemberId = memberId
            },
            cancellationToken);
        using var current = await member.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        using var repeat = await administrator.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = memberId
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            current.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            repeat.StatusCode);
        Assert.False(await context.Users.AnyAsync(
            account => account.Id == memberId,
            cancellationToken));
        Assert.True(await context.Users.AnyAsync(
            account => account.Id == administratorId,
            cancellationToken));
        Assert.False(await context.Wishlists.AnyAsync(
            list => list.Id == wishlist.Id,
            cancellationToken));
        var audit = await context.AdministrativeAccountErasureEvents.AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(
            administratorId,
            audit.AdministratorId);
        Assert.Equal(
            memberId,
            audit.MemberId);
        Assert.Equal(
            "SUPPORT-808",
            audit.RequestReference);
        Assert.Equal(
            confirmed ? AccountErasureNotificationStatus.Pending : AccountErasureNotificationStatus.NotApplicable,
            audit.NotificationStatus);
        var notification = await context.AccountErasureEmails.AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (confirmed)
        {
            Assert.NotNull(notification);
            Assert.NotNull(email);
            Assert.DoesNotContain(
                email,
                notification.ProtectedRecipient);
            Assert.Equal(
                email,
                scope.ServiceProvider.GetRequiredService<IAccountErasureRecipientProtector>()
                    .Read(
                        audit.Id,
                        notification.ProtectedRecipient));
            Assert.Equal(
                notification.CreatedAt.AddHours(24),
                notification.ExpiresAt);
        }
        else
        {
            Assert.Null(notification);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenTargetIsActingAdministrator_RejectsWithoutMutation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(administratorId),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = administratorId
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(
            "ACCOUNT_SELF_ERASURE_NOT_ALLOWED",
            body.GetProperty("errorCode").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.True(await context.Users.AnyAsync(
            account => account.Id == administratorId,
            cancellationToken));
        Assert.Empty(await context.AdministrativeAccountErasureEvents.ToArrayAsync(cancellationToken));
        Assert.Empty(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOnlyAdministratorCookieIsPresent_RejectsAndPreservesSession()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionService>();
        var session = await sessions.CreateAsync(
            administratorId,
            isPersistent: false,
            requestedSessionId: null,
            currentSessionId: null,
            cancellationToken);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(cancellationToken);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"MonKado.Refresh={session.RefreshToken}");
        client.DefaultRequestHeaders.Add(
            "Origin",
            "https://foreign.example.test");

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = memberId
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AdministrativeAccountErasureEvents.ToArrayAsync(cancellationToken));
        Assert.True(await context.Users.AnyAsync(
            account => account.Id == memberId,
            cancellationToken));
        Assert.NotNull(await sessions.ProveCurrentSessionAsync(
            session.RefreshToken,
            cancellationToken));
    }

    [Fact]
    public async Task PurgeAsync_WhenRecipientExpires_DiscardsItAndRetainsMinimalAudit()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var operationId = await scope.ServiceProvider.GetRequiredService<IAdministrativeAccountErasureService>()
            .ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-808",
                cancellationToken);
        var maintenance = scope.ServiceProvider.GetRequiredService<IAccountErasureMaintenance>();
        _clock.Advance(TimeSpan.FromHours(24));

        // Act
        await maintenance.PurgeAsync(cancellationToken);
        await maintenance.PurgeAsync(cancellationToken);

        // Assert
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AccountErasureEmails.AsNoTracking()
            .ToArrayAsync(cancellationToken));
        var audit = await context.AdministrativeAccountErasureEvents.AsNoTracking()
            .SingleAsync(cancellationToken);
        Assert.Equal(
            operationId,
            audit.Id);
        Assert.Equal(
            AccountErasureNotificationStatus.Failed,
            audit.NotificationStatus);
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(params IInterceptor[] interceptors)
    {
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider: _clock,
            configureServices: services => services.ConfigureDbContext<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptors)),
            giftImageStoragePath: Path.Combine(
                _storagePath,
                "images"),
            configureHost: builder => builder.UseSetting(
                "PersonalDataExports:StoragePath",
                Path.Combine(
                    _storagePath,
                    "exports")));
    }

    [Fact]
    public async Task ExecuteAsync_WhenImagesAndArchiveExist_InvalidatesAccessAndQueuesPhysicalCleanup()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            memberId,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = await context.Users.SingleAsync(
            account => account.Id == memberId,
            cancellationToken);
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "Private gift",
            null,
            null,
            null,
            1);
        using var bitmap = new SKBitmap(
            1,
            1);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(
            SKEncodedImageFormat.Webp,
            82);
        var bytes = encoded.ToArray();
        var giftImageId = Guid.CreateVersion7();
        var profileImageId = Guid.CreateVersion7();
        wish.ReplaceImage(
            giftImageId,
            SHA256.HashData(bytes));
        member.SetProfileImage(
            profileImageId,
            SHA256.HashData(bytes));
        context.Wishes.Add(wish);
        await context.SaveChangesAsync(cancellationToken);
        var imageStore = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        foreach (var id in new[]
        {
            giftImageId,
            profileImageId
        })
        {
            await imageStore.WritePendingAsync(
                id,
                bytes,
                cancellationToken);
            await imageStore.MarkCommittedAsync(
                id,
                cancellationToken);
        }

        var export = await scope.ServiceProvider.GetRequiredService<IPersonalDataExportService>()
            .RequestAsync(
                memberId,
                cancellationToken);
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var work = await jobs.ClaimAsync(cancellationToken);
        Assert.NotNull(work);
        var archive = await scope.ServiceProvider.GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
                work,
                cancellationToken);
        Assert.True(await jobs.CompleteAsync(
            work,
            archive,
            cancellationToken));
        var storedArchiveId = await context.MemberDataExports.Where(candidate => candidate.Id == export.Id)
            .Select(candidate => candidate.ArchiveId)
            .SingleAsync(cancellationToken);
        Assert.NotNull(storedArchiveId);
        using var owner = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            TestContext.Current.CancellationToken);
        var wishBody = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/v1/wishlists/{wishlist.Id:D}/wishes/{wish.Id:D}",
            cancellationToken);
        var imageUrl = Assert.IsType<string>(wishBody.GetProperty("imageUrl").GetString());
        using var visitor = factory.CreateClient();
        using var beforeImage = await visitor.GetAsync(
            imageUrl,
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            beforeImage.StatusCode);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);

        // Act
        using var response = await administrator.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = memberId
            },
            cancellationToken);
        using var obsoleteImage = await visitor.GetAsync(
            imageUrl,
            cancellationToken);
        using var obsoleteArchive = await owner.GetAsync(
            $"/api/v1/members/current/data-exports/{export.Id:D}/archive",
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            obsoleteImage.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            obsoleteArchive.StatusCode);
        var queuedImages = await context.GiftImageDeletionOutboxMessages.AsNoTracking()
            .Select(message => message.ImageId)
            .ToArrayAsync(cancellationToken);
        Assert.Equal(
            new[]
            {
                giftImageId,
                profileImageId
            }.Order(),
            queuedImages.Order());
        Assert.Null(await context.MemberDataExports.Where(candidate => candidate.Id == export.Id)
            .Select(candidate => candidate.MemberId)
            .SingleAsync(cancellationToken));
        await jobs.CleanupAsync(cancellationToken);
        await jobs.CleanupAsync(cancellationToken);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IPersonalDataExportStore>()
            .OpenReadAsync(
                export.Id,
                storedArchiveId.Value,
                cancellationToken));
        Assert.Empty(await context.MemberDataExports.AsNoTracking().ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenCommitAcknowledgementFails_ConfirmsOnlyDurableErasure(bool committed)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new WishlistModerationCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);

        if (committed)
            interceptor.Arm();
        else
            interceptor.ArmBeforeCommit();

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = memberId
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            committed ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            !committed,
            await context.Users.AnyAsync(
                member => member.Id == memberId,
                cancellationToken));
        Assert.Equal(
            committed ? 1 : 0,
            await context.AdministrativeAccountErasureEvents.CountAsync(cancellationToken));
        Assert.Equal(
            committed ? 1 : 0,
            await context.AccountErasureEmails.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommittedOutcomeCannotBeRead_ReturnsUnavailableWithoutReplaying()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new AccountDeletionVerificationFailure();
        var interceptor = new AccountDeletionLostCommitInterceptor(failure);
        await using var factory = await CreateFactoryAsync(
            interceptor,
            failure);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        interceptor.Armed = true;

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = memberId
            },
            cancellationToken);
        failure.Unavailable = false;

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await context.Users.AnyAsync(
            member => member.Id == memberId,
            cancellationToken));
        Assert.Single(await context.AdministrativeAccountErasureEvents.ToArrayAsync(cancellationToken));
        Assert.Single(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("missing")]
    [InlineData("unconfirmed")]
    public async Task ExecuteAsync_WhenActorLosesAccess_RechecksServiceBoundaryWithoutErasing(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        switch (scenario)
        {
            case "member":
                await context.UserRoles.Where(role => role.UserId == administratorId)
                    .ExecuteDeleteAsync(cancellationToken);
                break;
            case "missing":
                await context.Users.Where(member => member.Id == administratorId)
                    .ExecuteDeleteAsync(cancellationToken);
                break;
            case "unconfirmed":
                await context.Users.Where(member => member.Id == administratorId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            member => member.EmailConfirmed,
                            false),
                        cancellationToken);
                break;
        }

        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeAccountErasureService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.ExecuteAsync(
            administratorId,
            memberId,
            "SUPPORT-808",
            cancellationToken));

        // Assert
        if (scenario == "member")
            Assert.IsType<AdministratorAccessDeniedException>(exception);
        else
            Assert.IsType<InvalidAuthenticationSessionException>(exception);
        Assert.True(await context.Users.AnyAsync(
            member => member.Id == memberId,
            cancellationToken));
        Assert.Empty(await context.AdministrativeAccountErasureEvents.ToArrayAsync(cancellationToken));
        Assert.Empty(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task ClaimAsync_WhenTwoWorkersRace_OnlyOneOwnsRecipientAndCompletionPurgesIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var operationId = await CreateErasureAsync(factory);
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IAccountErasureEmailRepository>();
        var second = secondScope.ServiceProvider.GetRequiredService<IAccountErasureEmailRepository>();
        var now = _clock.GetUtcNow().UtcDateTime;

        // Act
        var claims = await Task.WhenAll(
            first.ClaimAsync(
                now,
                TimeSpan.FromMinutes(2),
                10,
                cancellationToken),
            second.ClaimAsync(
                now,
                TimeSpan.FromMinutes(2),
                10,
                cancellationToken));
        var claim = Assert.Single(
            claims,
            candidate => candidate is not null);
        Assert.NotNull(claim);
        var completed = await first.CompleteAsync(
            claim,
            now,
            AccountErasureNotificationStatus.Accepted,
            now,
            cancellationToken);
        var repeated = await second.CompleteAsync(
            claim,
            now,
            AccountErasureNotificationStatus.Failed,
            now,
            cancellationToken);

        // Assert
        Assert.True(completed);
        Assert.False(repeated);
        Assert.Equal(
            operationId,
            claim.OperationId);
        Assert.Equal(
            1,
            claim.AttemptCount);
        var context = firstScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
        Assert.Equal(
            AccountErasureNotificationStatus.Accepted,
            await context.AdministrativeAccountErasureEvents
                .Select(audit => audit.NotificationStatus)
                .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task CompleteAsync_WhenLeaseIsLost_RejectsOldAcknowledgementAndAllowsRetry()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        await CreateErasureAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountErasureEmailRepository>();
        var now = _clock.GetUtcNow().UtcDateTime;
        var original = await repository.ClaimAsync(
            now,
            TimeSpan.FromMinutes(2),
            10,
            cancellationToken);
        Assert.NotNull(original);
        _clock.Advance(TimeSpan.FromMinutes(2));
        now = _clock.GetUtcNow().UtcDateTime;
        var replacement = await repository.ClaimAsync(
            now,
            TimeSpan.FromMinutes(2),
            10,
            cancellationToken);
        Assert.NotNull(replacement);

        // Act
        var stale = await repository.CompleteAsync(
            original,
            now,
            AccountErasureNotificationStatus.Accepted,
            now,
            cancellationToken);
        var retry = await repository.CompleteAsync(
            replacement,
            now,
            AccountErasureNotificationStatus.Pending,
            now.AddMinutes(5),
            cancellationToken);
        var premature = await repository.ClaimAsync(
            now,
            TimeSpan.FromMinutes(2),
            10,
            cancellationToken);

        // Assert
        Assert.False(stale);
        Assert.True(retry);
        Assert.Null(premature);
        Assert.Equal(
            2,
            replacement.AttemptCount);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            AccountErasureNotificationStatus.Pending,
            await context.AdministrativeAccountErasureEvents
                .Select(audit => audit.NotificationStatus)
                .SingleAsync(cancellationToken));
        Assert.Single(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task PurgeAsync_WhenAttemptLimitIsReached_PreservesActiveLeaseThenDiscardsRecipient()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        await CreateErasureAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.AccountErasureEmails.ExecuteUpdateAsync(
            setters => setters.SetProperty(
                message => message.AttemptCount,
                9),
            cancellationToken);
        var repository = scope.ServiceProvider.GetRequiredService<IAccountErasureEmailRepository>();
        var claim = await repository.ClaimAsync(
            _clock.GetUtcNow().UtcDateTime,
            TimeSpan.FromMinutes(2),
            10,
            cancellationToken);
        Assert.NotNull(claim);
        var maintenance = scope.ServiceProvider.GetRequiredService<IAccountErasureMaintenance>();

        // Act
        await maintenance.PurgeAsync(cancellationToken);
        var activeRecipients = await context.AccountErasureEmails.CountAsync(cancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await maintenance.PurgeAsync(cancellationToken);

        // Assert
        Assert.Equal(
            1,
            activeRecipients);
        Assert.Empty(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
        Assert.Equal(
            AccountErasureNotificationStatus.Failed,
            await context.AdministrativeAccountErasureEvents
                .Select(audit => audit.NotificationStatus)
                .SingleAsync(cancellationToken));
    }

    [Fact]
    public async Task PurgeAsync_WhenSixCalendarMonthsElapse_RemovesAuditAtMonthEnd()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var createdAt = new DateTimeOffset(
            2026,
            8,
            31,
            12,
            0,
            0,
            TimeSpan.Zero);
        _clock.Advance(createdAt - _clock.GetUtcNow());
        await using var factory = await CreateFactoryAsync();
        await CreateErasureAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<IAccountErasureMaintenance>();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        _clock.Advance(createdAt.AddMonths(6) - createdAt - TimeSpan.FromSeconds(1));

        // Act
        await maintenance.PurgeAsync(cancellationToken);
        var beforeDeadline = await context.AdministrativeAccountErasureEvents.CountAsync(cancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await maintenance.PurgeAsync(cancellationToken);
        await maintenance.PurgeAsync(cancellationToken);

        // Assert
        Assert.Equal(
            1,
            beforeDeadline);
        Assert.Empty(await context.AdministrativeAccountErasureEvents.ToArrayAsync(cancellationToken));
        Assert.Empty(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenAnotherErasureOrCreationRaces_CommitsOneErasureWithoutRecreatingData(bool createWishlist)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var barrier = new AccountDeletionCommitBarrier();
        await using var factory = await CreateFactoryAsync(barrier);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            TestContext.Current.CancellationToken);
        using var concurrentClient = await AuthenticationTestData.CreateClientAsync(
            factory,
            createWishlist ? memberId : administratorId,
            TestContext.Current.CancellationToken);
        var request = new
        {
            requestReference = "SUPPORT-808",
            confirmedMemberId = memberId
        };
        barrier.Arm();

        // Act
        var firstTask = administrator.PostAsJsonAsync(
            GetRoute(memberId),
            request,
            cancellationToken);
        await barrier.WaitUntilEnteredAsync(cancellationToken);
        Task<HttpResponseMessage> concurrentTask;
        try
        {
            concurrentTask = createWishlist ? concurrentClient.PostAsJsonAsync(
                "/api/v1/wishlists",
                new
                {
                    name = "Concurrent list",
                    occasion = "other"
                },
                cancellationToken) : concurrentClient.PostAsJsonAsync(
                GetRoute(memberId),
                request,
                cancellationToken);
        }
        finally
        {
            barrier.Release();
        }

        using var first = await firstTask;
        using var concurrent = await concurrentTask;

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            first.StatusCode);
        Assert.Equal(
            createWishlist ? HttpStatusCode.Unauthorized : HttpStatusCode.NotFound,
            concurrent.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await context.Users.AnyAsync(
            member => member.Id == memberId,
            cancellationToken));
        Assert.Empty(await context.Wishlists.ToArrayAsync(cancellationToken));
        Assert.Single(await context.AdministrativeAccountErasureEvents.ToArrayAsync(cancellationToken));
        Assert.Single(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenBearerAdministratorRoleIsRevoked_DeniesWithoutErasing(bool revoked)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var actorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            revoked ? actorId : memberId,
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.UserRoles.Where(role => role.UserId == actorId)
            .ExecuteDeleteAsync(cancellationToken);

        // Act
        using var response = await client.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-808",
                confirmedMemberId = memberId
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(await context.Users.AnyAsync(
            member => member.Id == memberId,
            cancellationToken));
        Assert.Empty(await context.AdministrativeAccountErasureEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task DispatchAsync_WhenProviderRecovers_RetriesDurablyAndRemovesRecipientAfterAcknowledgement()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var operationId = await CreateErasureAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var message = await context.AccountErasureEmails.AsNoTracking()
            .SingleAsync(cancellationToken);
        var protector = scope.ServiceProvider.GetRequiredService<IAccountErasureRecipientProtector>();
        var recipient = protector.Read(
            operationId,
            message.ProtectedRecipient);
        Assert.NotNull(recipient);
        var senderMock = new Mock<IAccountErasureEmailSender>(MockBehavior.Strict);
        senderMock.SetupSequence(sender => sender.SendAsync(
                operationId,
                recipient,
                message.CreatedAt,
                It.Is<CancellationToken>(token => token.CanBeCanceled)))
            .ThrowsAsync(new AccountErasureEmailDeliveryException(
                AccountErasureEmailFailure.Transient,
                null))
            .Returns(Task.CompletedTask);
        var dispatcher = new AccountErasureEmailDispatcher(
            scope.ServiceProvider.GetRequiredService<IAccountErasureEmailRepository>(),
            protector,
            senderMock.Object,
            Microsoft.Extensions.Options.Options.Create(new Application.Options.AccountErasureProcessingOptions()),
            _clock,
            NullLogger<AccountErasureEmailDispatcher>.Instance);

        // Act
        var failedAttempt = await dispatcher.DispatchAsync(cancellationToken);
        var pending = await context.AccountErasureEmails.AsNoTracking()
            .SingleAsync(cancellationToken);
        var premature = await dispatcher.DispatchAsync(cancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var acceptedAttempt = await dispatcher.DispatchAsync(cancellationToken);

        // Assert
        Assert.Equal(
            1,
            failedAttempt);
        Assert.Equal(
            1,
            pending.AttemptCount);
        Assert.Null(pending.LeaseId);
        Assert.Equal(
            0,
            premature);
        Assert.Equal(
            1,
            acceptedAttempt);
        Assert.Empty(await context.AccountErasureEmails.ToArrayAsync(cancellationToken));
        Assert.Equal(
            AccountErasureNotificationStatus.Accepted,
            await context.AdministrativeAccountErasureEvents.Select(audit => audit.NotificationStatus)
                .SingleAsync(cancellationToken));
        senderMock.Verify(sender => sender.SendAsync(
                operationId,
                recipient,
                message.CreatedAt,
                It.Is<CancellationToken>(token => token.CanBeCanceled)),
            Times.Exactly(2));
        senderMock.VerifyNoOtherCalls();
    }

    private static async Task<Guid> CreateErasureAsync(PostgreSqlApiFactory factory)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IAdministrativeAccountErasureService>()
            .ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-808",
                cancellationToken);
    }

    private static string GetRoute(Guid memberId)
    {

        return $"/api/v1/admin/members/{memberId:D}/erasure-requests";
    }
}
