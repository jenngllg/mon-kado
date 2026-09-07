using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class PersonalDataExportIntegrationTests(PostgreSqlContainerFixture fixture) : IAsyncLifetime
{
    private const string Route = "/api/v1/members/current/data-exports";
    private readonly string _storagePath = Path.Combine(
        Path.GetTempPath(),
        $"monkado-export-integration-{Guid.CreateVersion7():N}");
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    public ValueTask InitializeAsync()
    {

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        var path = Path.GetFullPath(_storagePath);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetTempPath());

        if (Path.GetDirectoryName(path) != parent || !Path
            .GetFileName(path)
            .StartsWith(
            "monkado-export-integration-",
            StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test directory.");

        if (Directory.Exists(path))
            Directory.Delete(
                path,
                recursive: true);
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RequestAsync_WhenOnlyRefreshCookieIsPresent_RejectsRequestWithoutCreatingExport()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IRefreshSessionService>();
        var refreshSession = await sessions.CreateAsync(
            member.Id,
            isPersistent: false,
            requestedSessionId: null,
            currentSessionId: null,
            TestContext.Current.CancellationToken);
        await scope.ServiceProvider
            .GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(TestContext.Current.CancellationToken);
        using var cookieClient = factory.CreateClient();
        cookieClient.DefaultRequestHeaders.Add(
            "Cookie",
            $"MonKado.Refresh={refreshSession.RefreshToken}");
        cookieClient.DefaultRequestHeaders.Add(
            "Origin",
            "https://foreign.example.test");
        using var bearerClient = CreateClient(
            factory,
            member.Id);

        // Act
        using var response = await cookieClient.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        using var latest = await bearerClient.GetAsync(
            $"{Route}/latest",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            latest.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.NotNull(await sessions.ProveCurrentSessionAsync(
            refreshSession.RefreshToken,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RequestAsync_WhenGenerated_ExportsOwnedDataAndImagesWithoutCredentialsOrOtherMembersData()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var other = await CreateMemberAsync(factory);
        var profileBytes = Encoding.UTF8.GetBytes("profile-normalized-image");
        var wishBytes = Encoding.UTF8.GetBytes("wish-normalized-image");
        var wishId = await SeedOwnedImagesAsync(
            factory,
            owner,
            other,
            profileBytes,
            wishBytes);
        using var client = CreateClient(
            factory,
            owner.Id);

        // Act
        using var requested = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var request = await requested.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var exportId = request
            .GetProperty("id")
            .GetGuid();
        using var repeated = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var repeatedBody = await repeated.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        await GenerateAsync(factory);
        using var ready = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        using var response = await client.GetAsync(
            $"{Route}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            requested.StatusCode);
        Assert.Equal(
            $"{Route}/{exportId:D}",
            requested.Headers.Location?.OriginalString);
        Assert.Equal(
            "queued",
            request
                .GetProperty("status")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            request
                .GetProperty("snapshotAt")
                .ValueKind);
        Assert.Equal(
            exportId,
            repeatedBody
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            HttpStatusCode.OK,
            ready.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "application/zip",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "attachment",
            response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "nosniff",
            Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var zip = new ZipArchive(
            new MemoryStream(bytes),
            ZipArchiveMode.Read);
        Assert.Equal(
            4,
            zip.Entries.Count);
        await AssertImageAsync(
            zip,
            "images/profile.webp",
            profileBytes);
        await AssertImageAsync(
            zip,
            $"images/wishes/{wishId:D}.webp",
            wishBytes);
        var data = zip.GetEntry("data.json");
        Assert.NotNull(data);
        await using var dataStream = data.Open();
        using var json = await JsonDocument.ParseAsync(
            dataStream,
            cancellationToken: TestContext.Current.CancellationToken);
        var root = json.RootElement;
        Assert.Equal(
            1,
            root
                .GetProperty("schemaVersion")
                .GetInt32());
        Assert.Equal(
            owner.Id,
            root
                .GetProperty("account")
                .GetProperty("profile")
                .GetProperty("id")
                .GetGuid());
        Assert.Single(root
                .GetProperty("wishlists")
                .EnumerateArray());
        Assert.Equal(
            wishId,
            Assert
                .Single(root
                    .GetProperty("wishes")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Empty(root
                .GetProperty("reservations")
                .EnumerateArray());
        Assert.Single(root
                .GetProperty("accountActivity")
                .GetProperty("sessions")
                .EnumerateArray());
        var text = root.GetRawText();
        Assert.NotNull(other.Email);
        Assert.DoesNotContain(
            other.Email,
            text);
        Assert.DoesNotContain(
            "foreign-private-content",
            text);
        Assert.DoesNotContain(
            "passwordHash",
            text);
        Assert.DoesNotContain(
            "refreshTokenHash",
            text);
        Assert.DoesNotContain(
            "securityStamp",
            text);
        Assert.DoesNotContain(
            "contentHash",
            text);
        Assert.DoesNotContain(
            _storagePath,
            text);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.MemberDataExportId == exportId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_WhenForeignUnknownOrNotReady_PreservesIsolationAndCookies()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var other = await CreateMemberAsync(factory);
        using var ownerClient = CreateClient(
            factory,
            owner.Id);
        using var otherClient = CreateClient(
            factory,
            other.Id);
        using var anonymous = factory.CreateClient();
        using var requested = await ownerClient.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var request = await requested.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var exportId = request
            .GetProperty("id")
            .GetGuid();

        // Act
        using var foreign = await otherClient.GetAsync(
            $"{Route}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        using var notReady = await ownerClient.GetAsync(
            $"{Route}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        using var unauthenticated = await anonymous.GetAsync(
            $"{Route}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        using var unknown = await ownerClient.GetAsync(
            $"{Route}/{Guid.CreateVersion7():D}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            foreign.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            notReady.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unauthenticated.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            unknown.StatusCode);
        var error = await notReady.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(
            ErrorCodes.MemberDataExportNotReady,
            error?.ErrorCode);
        Assert.False(notReady.Headers.Contains("Set-Cookie"));
        Assert.False(foreign.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task DownloadAsync_WhenDeadlineReached_RejectsBeforeCleanupAndAllowsANewRequest()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            owner.Id);
        using var requested = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var request = await requested.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var exportId = request
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);
        _clock.Advance(TimeSpan.FromHours(24));

        // Act
        using var expired = await client.GetAsync(
            $"{Route}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        using var next = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var nextBody = await next.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        await jobs.CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            expired.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            next.StatusCode);
        Assert.NotEqual(
            exportId,
            nextBody
                .GetProperty("id")
                .GetGuid());
        Assert.False(Directory.Exists(Path.Combine(
                    _storagePath,
                    "exports",
                    exportId.ToString("N"))));
    }

    [Fact]
    public async Task CompleteAsync_WhenLeaseWasTakenOver_RejectsTheOldWorkerAndPublishesOnlyTheNewAttempt()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            owner.Id);
        using var requested = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        await using var firstScope = factory.Services.CreateAsyncScope();
        var firstJobs = firstScope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var first = await firstJobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        var firstArchive = await firstScope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            first,
            TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(3));
        await using var secondScope = factory.Services.CreateAsyncScope();
        var secondJobs = secondScope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var second = await secondJobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(second);
        var secondArchive = await secondScope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            second,
            TestContext.Current.CancellationToken);

        // Act
        var staleRenewal = await firstJobs.RenewAsync(
            first,
            TestContext.Current.CancellationToken);
        var stalePublication = await firstJobs.CompleteAsync(
            first,
            firstArchive,
            TestContext.Current.CancellationToken);
        var publication = await secondJobs.CompleteAsync(
            second,
            secondArchive,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(staleRenewal);
        Assert.False(stalePublication);
        Assert.True(publication);
        var context = secondScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            second.LeaseId,
            await context.MemberDataExports
                .Select(export => export.ArchiveId)
                .SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.MemberDataExportId == second.ExportId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupAsync_WhenAccountDeleted_PreservesCleanupIdentityAndRemovesPrivateArchive()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            owner.Id);
        using var requested = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var request = await requested.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var exportId = request
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == owner.Id)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        var detached = await context.MemberDataExports
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        // Act
        using var unavailable = await client.GetAsync(
            $"{Route}/{exportId:D}/archive",
            TestContext.Current.CancellationToken);
        await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportJobs>()
            .CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(detached.MemberId);
        Assert.False(detached.OwnsLease(
                Guid.CreateVersion7(),
                _clock
                    .GetUtcNow()
                    .UtcDateTime));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unavailable.StatusCode);
        Assert.False(unavailable.Headers.Contains("Set-Cookie"));
        Assert.False(await context.MemberDataExports.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(Path.Combine(
                    _storagePath,
                    "exports",
                    exportId.ToString("N"))));
    }

    [Fact]
    public async Task RequestAsync_WhenRequestsRace_CreatesOnlyOneDurableRequest()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);

        // Act
        var responses = await Task.WhenAll(Enumerable
                .Range(
                0,
                8)
                .Select(_ => client.PostAsync(
                    Route,
                    null,
                    TestContext.Current.CancellationToken)));
        try
        {
            var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)));

            // Assert
            Assert.All(
                responses,
                response => Assert.Equal(
                    HttpStatusCode.Accepted,
                    response.StatusCode));
            Assert.Single(bodies
                    .Select(body => body
                        .GetProperty("id")
                        .GetGuid())
                    .Distinct());
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Equal(
                1,
                await scope.ServiceProvider
                    .GetRequiredService<MonKadoDbContext>()
                    .MemberDataExports.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task RequestAsync_WhenFailedRequestsAreCleaned_KeepsRollingQuotaUntilItsExactBoundary()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        for (var index = 0; index < 3; index++)
        {
            using var requested = await client.PostAsync(
                Route,
                null,
                TestContext.Current.CancellationToken);
            Assert.Equal(
                HttpStatusCode.Accepted,
                requested.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
            var work = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(work);
            await jobs.FailAsync(
                work,
                PersonalDataExportFailure.TooLarge,
                TestContext.Current.CancellationToken);
            await jobs.CleanupAsync(TestContext.Current.CancellationToken);
        }

        // Act
        using var limited = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromHours(24));
        using var next = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        await using var cleanupScope = factory.Services.CreateAsyncScope();
        await cleanupScope.ServiceProvider
            .GetRequiredService<IPersonalDataExportJobs>()
            .CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limited.StatusCode);
        var error = await limited.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.Equal(
            ErrorCodes.MemberDataExportRateLimited,
            error?.ErrorCode);
        Assert.False(limited.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            HttpStatusCode.Accepted,
            next.StatusCode);
        Assert.Equal(
            1,
            await cleanupScope.ServiceProvider
                .GetRequiredService<MonKadoDbContext>()
                .MemberDataExports.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAsync_WhenCommitAcknowledgementIsLost_VerifiesWithoutReplayingCreation(bool verificationUnavailable)
    {
        // Arrange
        var failure = new AccountDeletionVerificationFailure();
        var unavailableCommit = new AccountDeletionLostCommitInterceptor(failure);
        var confirmedCommit = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(
            failure,
            unavailableCommit,
            confirmedCommit);
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);

        if (verificationUnavailable)
            unavailableCommit.Armed = true;
        else
            confirmedCommit.Arm();

        // Act
        using var response = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        failure.Unavailable = false;
        using var repeated = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            verificationUnavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted,
            response.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            repeated.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(
            1,
            await scope.ServiceProvider
                .GetRequiredService<MonKadoDbContext>()
                .MemberDataExports.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestAsync_WhenAccountBecomesUnavailableDuringCommitVerification_ReturnsUnauthorized(bool deleted)
    {
        // Arrange
        var interceptor = new ProfileImageCommitAccountChangeInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        interceptor.AfterCommit = async token =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var query = scope.ServiceProvider
                .GetRequiredService<MonKadoDbContext>()
                .Users.Where(user => user.Id == member.Id);

            if (deleted)
                await query.ExecuteDeleteAsync(token);
            else
                await query.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.EmailConfirmed,
                        false),
                    token);
        };

        // Act
        using var response = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteAsync_WhenCommitOutcomeIsAmbiguous_ConfirmsOnlyDurableArchiveAndNotification(bool beforeCommit)
    {
        // Arrange
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        using var request = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var work = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(work);
        var archive = await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            work,
            TestContext.Current.CancellationToken);

        if (beforeCommit)
            interceptor.ArmBeforeCommit();
        else
            interceptor.Arm();

        // Act
        if (beforeCommit)
            await Assert.ThrowsAsync<DependencyUnavailableException>(() => jobs.CompleteAsync(
                    work,
                    archive,
                    TestContext.Current.CancellationToken));
        else
            Assert.True(await jobs.CompleteAsync(
                    work,
                    archive,
                    TestContext.Current.CancellationToken));
        using var latest = await client.GetAsync(
            $"{Route}/latest",
            TestContext.Current.CancellationToken);
        var json = await latest.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            beforeCommit ? "processing" : "ready",
            json
                .GetProperty("status")
                .GetString());
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            beforeCommit ? 0 : 1,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.MemberDataExportId == work.ExportId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailAsync_WhenGenerationFails_UsesAllRetryWindowsThenPublishesTerminalFailure()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        using var request = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        var delays = new[]
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1)
        };
        await using var scope = factory.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var leases = new HashSet<Guid>();

        // Act
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var work = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(work);
            Assert.Equal(
                attempt,
                work.AttemptCount);
            Assert.True(leases.Add(work.LeaseId));
            await jobs.FailAsync(
                work,
                PersonalDataExportFailure.GenerationFailed,
                TestContext.Current.CancellationToken);
            Assert.Null(await jobs.ClaimAsync(TestContext.Current.CancellationToken));

            if (attempt < 5)
                _clock.Advance(delays[attempt - 1]);
        }

        var json = await client.GetFromJsonAsync<JsonElement>(
            $"{Route}/latest",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "failed",
            json
                .GetProperty("status")
                .GetString());
        Assert.Equal(
            ErrorCodes.MemberDataExportGenerationFailed,
            json
                .GetProperty("errorCode")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            json
                .GetProperty("sizeInBytes")
                .ValueKind);
        Assert.Equal(
            0,
            await scope.ServiceProvider
                .GetRequiredService<MonKadoDbContext>()
                .AuthenticationEmailOutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadAsync_WhenPublishedArchiveIsMissingOrTruncated_RejectsIncompleteContent(bool truncated)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        using var request = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        await GenerateAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var export = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .MemberDataExports
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var path = Path.Combine(
            _storagePath,
            "exports",
            export.Id.ToString("N"),
            $"{export.ArchiveId:N}.zip");

        if (truncated)
        {
            await using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None);
            file.SetLength(1);
        }
        else
            File.Delete(path);

        // Act
        using var response = await client.GetAsync(
            $"{Route}/{export.Id:D}/archive",
            TestContext.Current.CancellationToken);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error?.ErrorCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(
            _storagePath,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("read")]
    [InlineData("download")]
    [InlineData("claim")]
    [InlineData("renew")]
    [InlineData("complete")]
    [InlineData("fail")]
    [InlineData("cleanup")]
    [InlineData("snapshot")]
    public async Task Services_WhenPostgreSqlIsUnavailable_TranslateDependencyFailures(string operation)
    {
        // Arrange
        var outage = new AccountDeletionVerificationFailure();
        await using var factory = await CreateFactoryAsync(outage);
        var member = await CreateMemberAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalDataExportService>();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var reader = scope.ServiceProvider.GetRequiredService<IPersonalDataExportSnapshotReader>();
        var work = new PersonalDataExportWorkItem
        {
            MemberId = member.Id,
            ExportId = Guid.CreateVersion7(),
            LeaseId = Guid.CreateVersion7(),
            AttemptCount = 1
        };
        var archive = new PersonalDataExportArchive
        {
            SizeInBytes = 10,
            SnapshotAt = _clock
                .GetUtcNow()
                .UtcDateTime
        };
        using var data = new MemoryStream();
        using var manifest = new MemoryStream();
        outage.Unavailable = true;

        // Act
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() => operation switch
            {
                "request" => service.RequestAsync(
                    member.Id,
                    TestContext.Current.CancellationToken),
                "read" => service.GetAsync(
                    member.Id,
                    null,
                    TestContext.Current.CancellationToken),
                "download" => service.OpenArchiveAsync(
                    member.Id,
                    work.ExportId,
                    TestContext.Current.CancellationToken),
                "claim" => jobs.ClaimAsync(TestContext.Current.CancellationToken),
                "renew" => jobs.RenewAsync(
                    work,
                    TestContext.Current.CancellationToken),
                "complete" => jobs.CompleteAsync(
                    work,
                    archive,
                    TestContext.Current.CancellationToken),
                "fail" => jobs.FailAsync(
                    work,
                    PersonalDataExportFailure.GenerationFailed,
                    TestContext.Current.CancellationToken),
                "cleanup" => jobs.CleanupAsync(TestContext.Current.CancellationToken),
                _ => reader.WriteAsync(
                    member.Id,
                    data,
                    manifest,
                    TestContext.Current.CancellationToken)
            });

        // Assert
        Assert.Contains(
            "PostgreSQL",
            exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Services_WhenMemberIsMissingOrUnconfirmed_RejectRequestsAndSnapshotPublication(bool removed)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        if (removed)
            await context.Users
                .Where(user => user.Id == member.Id)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        else
            await context.Users
                .Where(user => user.Id == member.Id)
                .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.EmailConfirmed,
                    false),
                TestContext.Current.CancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<IPersonalDataExportService>();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var reader = scope.ServiceProvider.GetRequiredService<IPersonalDataExportSnapshotReader>();
        var work = new PersonalDataExportWorkItem
        {
            MemberId = member.Id,
            ExportId = Guid.CreateVersion7(),
            LeaseId = Guid.CreateVersion7(),
            AttemptCount = 1
        };
        using var data = new MemoryStream();
        using var manifest = new MemoryStream();

        // Act
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(() => service.RequestAsync(
                member.Id,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(() => service.GetAsync(
                member.Id,
                null,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(() => reader.WriteAsync(
                member.Id,
                data,
                manifest,
                TestContext.Current.CancellationToken));
        var published = await jobs.CompleteAsync(
            work,
            new PersonalDataExportArchive(),
            TestContext.Current.CancellationToken);
        await jobs.FailAsync(
            work,
            PersonalDataExportFailure.GenerationFailed,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(published);
        Assert.False(await context.AuthenticationEmailOutboxMessages.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClaimAsync_WhenCrashedWorkerExhaustedAttempts_MarksFailureInsteadOfClaimingAgain()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalDataExportService>();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        await service.RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.NotNull(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
            _clock.Advance(TimeSpan.FromMinutes(3));
        }

        // Act
        var next = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        var details = await service.GetAsync(
            member.Id,
            null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(next);
        Assert.Equal(
            PersonalDataExportStatus.Failed,
            details.Status);
        Assert.Equal(
            PersonalDataExportFailure.GenerationFailed,
            details.Failure);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("deleted")]
    [InlineData("unconfirmed")]
    [InlineData("length-io")]
    [InlineData("length-access")]
    [InlineData("dispose-io")]
    [InlineData("dispose-access")]
    public async Task OpenArchiveAsync_WhenStateChangesWhileOpening_RechecksAccessAndDisposesWithoutLeakingStorageErrors(string scenario)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        using var request = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        await GenerateAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var export = await context.MemberDataExports
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        var streamMock = new Mock<Stream>(MockBehavior.Strict);
        var storeMock = new Mock<IPersonalDataExportStore>(MockBehavior.Strict);

        if (scenario == "length-io")
            streamMock
                .SetupGet(stream => stream.Length)
                .Throws(new IOException("private-path"));
        else
            if (scenario == "length-access")
                streamMock
                    .SetupGet(stream => stream.Length)
                    .Throws(new UnauthorizedAccessException("private-path"));
            else
                streamMock
                    .SetupGet(stream => stream.Length)
                    .Returns(scenario.StartsWith(
                        "dispose",
                        StringComparison.Ordinal) ? 0 : export.SizeInBytes.GetValueOrDefault());

        if (scenario == "dispose-io")
            streamMock
                .Setup(stream => stream.DisposeAsync())
                .Throws(new IOException("private-path"));
        else
            if (scenario == "dispose-access")
                streamMock
                    .Setup(stream => stream.DisposeAsync())
                    .Throws(new UnauthorizedAccessException("private-path"));
            else
                streamMock
                    .Setup(stream => stream.DisposeAsync())
                    .Returns(ValueTask.CompletedTask);
        storeMock
            .Setup(store => store.OpenReadAsync(
                export.Id,
                export.ArchiveId.GetValueOrDefault(),
                TestContext.Current.CancellationToken))
            .Returns(async () =>
            {

                if (scenario == "expired")
                    _clock.Advance(TimeSpan.FromHours(24));

                if (scenario == "deleted")
                    await context.Users
                        .Where(user => user.Id == member.Id)
                        .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

                if (scenario == "unconfirmed")
                    await context.Users
                        .Where(user => user.Id == member.Id)
                        .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            user => user.EmailConfirmed,
                            false),
                        TestContext.Current.CancellationToken);

                return streamMock.Object;
            });
        var service = new PersonalDataExportService(
            context,
            context,
            scope.ServiceProvider.GetRequiredService<IMonKadoUserRepository>(),
            storeMock.Object,
            scope.ServiceProvider.GetRequiredService<IOptions<Application.Options.PersonalDataExportOptions>>(),
            _clock,
            factory.Services.GetRequiredService<IServiceScopeFactory>());

        // Act
        var exception = await Record.ExceptionAsync(() => service.OpenArchiveAsync(
                member.Id,
                export.Id,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.NotNull(exception);

        if (scenario == "expired")
            Assert.IsType<PersonalDataExportNotFoundException>(exception);
        else
            if (scenario is "deleted" or "unconfirmed")
                Assert.IsType<InvalidAuthenticationSessionException>(exception);
            else
                Assert.IsType<PersonalDataExportStorageUnavailableException>(exception);
        Assert.DoesNotContain(
            "private-path",
            exception.ToString());
        storeMock.Verify(
            store => store.OpenReadAsync(
                export.Id,
                export.ArchiveId.GetValueOrDefault(),
                TestContext.Current.CancellationToken),
            Times.Once);
        streamMock.VerifyGet(
            stream => stream.Length,
            Times.Once);
        streamMock.Verify(
            stream => stream.DisposeAsync(),
            Times.Once);
        storeMock.VerifyNoOtherCalls();
        streamMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CompleteAsync_WhenVerificationIsUnavailable_ReportsDependencyFailureWithoutDuplicatingNotification()
    {
        // Arrange
        var outage = new AccountDeletionVerificationFailure();
        var interceptor = new AccountDeletionLostCommitInterceptor(outage);
        await using var factory = await CreateFactoryAsync(
            outage,
            interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        using var request = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var work = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(work);
        var archive = await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            work,
            TestContext.Current.CancellationToken);
        interceptor.Armed = true;

        // Act
        await Assert.ThrowsAsync<DependencyUnavailableException>(() => jobs.CompleteAsync(
                work,
                archive,
                TestContext.Current.CancellationToken));
        outage.Unavailable = false;
        var replay = await jobs.CompleteAsync(
            work,
            archive,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(replay);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.MemberDataExportId == work.ExportId,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            PersonalDataExportStatus.Ready,
            await context.MemberDataExports
                .Select(export => export.Status)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupAsync_WhenOldFilesAreReferenced_ProtectsLiveAndReadyAttemptsButDeletesOrphans()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPersonalDataExportService>();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        await service.RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);
        var work = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(work);
        var archive = await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            work,
            TestContext.Current.CancellationToken);
        var directory = Path.Combine(
            _storagePath,
            "exports",
            work.ExportId.ToString("N"));
        var archivePath = Path.Combine(
            directory,
            $"{work.LeaseId:N}.zip");
        var abandonedPath = Path.Combine(
            directory,
            $"{Guid.CreateVersion7():N}.tmp");
        await File.WriteAllTextAsync(
            abandonedPath,
            "incomplete",
            TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(
            archivePath,
            _clock
                .GetUtcNow()
                .UtcDateTime.AddHours(-2));
        File.SetLastWriteTimeUtc(
            abandonedPath,
            _clock
                .GetUtcNow()
                .UtcDateTime.AddHours(-2));

        // Act
        await jobs.CleanupAsync(TestContext.Current.CancellationToken);
        var protectedWhileProcessing = File.Exists(archivePath);
        Assert.True(await jobs.CompleteAsync(
                work,
                archive,
                TestContext.Current.CancellationToken));
        await jobs.CleanupAsync(TestContext.Current.CancellationToken);
        await jobs.CleanupAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(protectedWhileProcessing);
        Assert.True(File.Exists(archivePath));
        Assert.False(File.Exists(abandonedPath));
    }

    [Fact]
    public async Task WriteAsync_WhenWishlistChangesDuringSnapshot_KeepsOneRepeatableReadView()
    {
        // Arrange
        var barrier = new PersonalDataExportSnapshotBarrier();
        await using var factory = await CreateFactoryAsync(barrier);
        var member = await CreateMemberAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wishlist = new Wishlist(
            Guid.CreateVersion7(),
            member.Id,
            "Before",
            "BEFORE",
            WishlistOccasion.Other,
            null,
            null);
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "Before",
            null,
            null,
            null,
            1);
        context.Wishlists.Add(wishlist);
        context.Wishes.Add(wish);
        await context.Users
            .Where(user => user.Id == member.Id)
            .ExecuteUpdateAsync(
            setters => setters.SetProperty(
                user => user.LockoutEnd,
                new DateTimeOffset(
                    2026,
                    9,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero)),
            TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var data = new MemoryStream();
        using var manifest = new MemoryStream();
        barrier.Arm();

        // Act
        var writing = scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportSnapshotReader>()
            .WriteAsync(
            member.Id,
            data,
            manifest,
            TestContext.Current.CancellationToken);
        var checkpoint = await Task.WhenAny(
            writing,
            barrier.Reached.Task);
        await checkpoint.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(barrier.Reached.Task.IsCompletedSuccessfully);
        try
        {
            await using var mutationScope = factory.Services.CreateAsyncScope();
            await mutationScope.ServiceProvider
                .GetRequiredService<MonKadoDbContext>()
                .Wishes
                .Where(candidate => candidate.Id == wish.Id)
                .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.Name,
                    "After"),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        await writing;
        using var json = JsonDocument.Parse(data.ToArray());
        using var imageManifest = JsonDocument.Parse(manifest.ToArray());

        // Assert
        Assert.Equal(
            "Before",
            Assert
                .Single(json.RootElement
                    .GetProperty("wishes")
                    .EnumerateArray())
                .GetProperty("name")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            Assert
                .Single(json.RootElement
                    .GetProperty("wishes")
                    .EnumerateArray())
                .GetProperty("imagePath")
                .ValueKind);
        Assert.Equal(
            DateTimeKind.Utc,
            json.RootElement
                .GetProperty("account")
                .GetProperty("authentication")
                .GetProperty("lockoutEnd")
                .GetDateTime()
                .Kind);
        Assert.Empty(imageManifest.RootElement.EnumerateArray());
        Assert.Equal(
            "After",
            await context.Wishes
                .Where(candidate => candidate.Id == wish.Id)
                .Select(candidate => candidate.Name)
                .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BuildAsync_WhenAnImageDisappeared_RetriesAWholeFreshSnapshotInsteadOfPublishingPartialData()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var other = await CreateMemberAsync(factory);
        var wishId = await SeedOwnedImagesAsync(
            factory,
            member,
            other,
            [1],
            [2]);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wish = await context.Wishes.SingleAsync(
            candidate => candidate.Id == wishId,
            TestContext.Current.CancellationToken);
        var imageStore = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        await imageStore.DeleteAsync(
            wish.ImageId.GetValueOrDefault(),
            TestContext.Current.CancellationToken);
        await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportService>()
            .RequestAsync(
            member.Id,
            TestContext.Current.CancellationToken);
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var first = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(first);
        var builder = scope.ServiceProvider.GetRequiredService<IPersonalDataExportArchiveBuilder>();

        // Act
        await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => builder.BuildAsync(
                first,
                TestContext.Current.CancellationToken));
        await jobs.FailAsync(
            first,
            PersonalDataExportFailure.GenerationFailed,
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        wish = await context.Wishes.SingleAsync(
            candidate => candidate.Id == wishId,
            TestContext.Current.CancellationToken);
        wish.RemoveImage();
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(second);
        var archive = await builder.BuildAsync(
            second,
            TestContext.Current.CancellationToken);
        Assert.True(await jobs.CompleteAsync(
                second,
                archive,
                TestContext.Current.CancellationToken));
        using var client = CreateClient(
            factory,
            member.Id);
        var bytes = await client.GetByteArrayAsync(
            $"{Route}/{second.ExportId:D}/archive",
            TestContext.Current.CancellationToken);
        using var zip = new ZipArchive(
            new MemoryStream(bytes),
            ZipArchiveMode.Read);
        var entry = zip.GetEntry("data.json");
        Assert.NotNull(entry);
        await using var dataStream = entry.Open();
        using var json = await JsonDocument.ParseAsync(
            dataStream,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(
            first.LeaseId,
            second.LeaseId);
        Assert.Equal(
            JsonValueKind.Null,
            Assert
                .Single(json.RootElement
                    .GetProperty("wishes")
                    .EnumerateArray())
                .GetProperty("imagePath")
                .ValueKind);
        Assert.Equal(
            3,
            zip.Entries.Count);
        Assert.NotNull(zip.GetEntry("images/profile.webp"));
        Assert.Null(zip.GetEntry($"images/wishes/{wishId:D}.webp"));
        Assert.Equal(
            1,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.MemberDataExportId == second.ExportId,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RequestAsync_WhenCommitFailsBeforePersistence_DoesNotConfirmAnAbsentRequest()
    {
        // Arrange
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = CreateClient(
            factory,
            member.Id);
        interceptor.ArmBeforeCommit();

        // Act
        using var response = await client.PostAsync(
            Route,
            null,
            TestContext.Current.CancellationToken);
        using var latest = await client.GetAsync(
            $"{Route}/latest",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            latest.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider
                .GetRequiredService<MonKadoDbContext>()
                .MemberDataExports.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteAsync_WhenMemberHasRetainedActivity_ExportsOnlyTheirAssociationsAndReservationHistory()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var other = await CreateMemberAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wishlist = new Wishlist(
            Guid.CreateVersion7(),
            other.Id,
            "Shared list",
            "SHARED LIST",
            WishlistOccasion.Other,
            null,
            null);
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "Shared gift",
            null,
            null,
            null,
            2);
        var participant = new WishlistParticipant(
            Guid.CreateVersion7(),
            wishlist.Id,
            Guid.CreateVersion7(),
            "Former guest name");
        participant.AttachToMember(member.Id);
        var reservation = new GiftReservation(
            Guid.CreateVersion7(),
            wishlist.Id,
            wish.Id,
            participant.Id,
            1);
        var now = _clock
            .GetUtcNow()
            .UtcDateTime;
        context.Wishlists.Add(wishlist);
        context.Wishes.Add(wish);
        context.WishlistParticipants.Add(participant);
        context.GiftReservations.Add(reservation);
        context.GiftReservationHistories.Add(new GiftReservationHistory(
                reservation.Id,
                member.Id,
                wishlist.Id,
                wishlist.Name,
                wish.Id,
                wish.Name,
                1,
                now,
                now));
        context.GiftReservationHistories.Add(new GiftReservationHistory(
                Guid.CreateVersion7(),
                other.Id,
                wishlist.Id,
                "foreign-retained-history",
                wish.Id,
                wish.Name,
                1,
                now,
                now));
        context.UserLogins.Add(new IdentityUserLogin<Guid>
        {
            UserId = member.Id,
            LoginProvider = "Google",
            ProviderKey = "member-google-association",
            ProviderDisplayName = "Google"
        });
        context.UserLogins.Add(new IdentityUserLogin<Guid>
        {
            UserId = other.Id,
            LoginProvider = "Google",
            ProviderKey = "foreign-google-association",
            ProviderDisplayName = "Google"
        });
        var notification = AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
            member.Id,
            now);
        context.AuthenticationEmailOutboxMessages.Add(notification);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var data = new MemoryStream();
        using var manifest = new MemoryStream();

        // Act
        await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportSnapshotReader>()
            .WriteAsync(
            member.Id,
            data,
            manifest,
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(data.ToArray());
        var root = document.RootElement;

        // Assert
        Assert.Empty(root
                .GetProperty("wishlists")
                .EnumerateArray());
        Assert.Empty(root
                .GetProperty("wishes")
                .EnumerateArray());
        Assert.Equal(
            "Former guest name",
            Assert
                .Single(root
                    .GetProperty("participations")
                    .EnumerateArray())
                .GetProperty("guestDisplayName")
                .GetString());
        Assert.Equal(
            reservation.Id,
            Assert
                .Single(root
                    .GetProperty("reservations")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            reservation.Id,
            Assert
                .Single(root
                    .GetProperty("reservationHistory")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            "member-google-association",
            Assert
                .Single(root
                    .GetProperty("account")
                    .GetProperty("externalAccounts")
                    .EnumerateArray())
                .GetProperty("providerUserId")
                .GetString());
        Assert.Equal(
            notification.Id,
            Assert
                .Single(root
                    .GetProperty("accountActivity")
                    .GetProperty("notifications")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.DoesNotContain(
            "foreign-retained-history",
            root.GetRawText());
        Assert.DoesNotContain(
            "foreign-google-association",
            root.GetRawText());
        Assert.DoesNotContain(
            "guestSessionId",
            root.GetRawText());
        Assert.DoesNotContain(
            "lastError",
            root.GetRawText());
        Assert.DoesNotContain(
            "securityStamp",
            root.GetRawText());
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(params IInterceptor[] interceptors)
    {
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider: _clock,
            configureServices: services =>
            {

                if (interceptors.Length > 0)
                    services.ConfigureDbContext<MonKadoDbContext>((
                            _,
                            options) => options.AddInterceptors(interceptors));
            },
            giftImageStoragePath: Path.Combine(
                _storagePath,
                "images"),
            configureHost: builder => builder.UseSetting(
                "PersonalDataExports:StoragePath",
                Path.Combine(
                    _storagePath,
                    "exports")));
    }

    private static async Task<MonKadoUser> CreateMemberAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var email = $"export-{Guid.CreateVersion7():N}@example.test";
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Jennifer",
            EmailConfirmed = true,
            SecurityStamp = "never-export-this-stamp"
        };
        context.Users.Add(member);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return member;
    }

    private static HttpClient CreateClient(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var tokenService = new JwtAccessTokenService(
            factory.Services.GetRequiredService<IOptions<JwtOptions>>(),
            TimeProvider.System);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokenService
                .Create(memberId)
                .Value);

        return client;
    }

    private static async Task GenerateAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var workItem = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(workItem);
        var archive = await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            workItem,
            TestContext.Current.CancellationToken);
        Assert.True(await jobs.CompleteAsync(
                workItem,
                archive,
                TestContext.Current.CancellationToken));
    }

    private async Task<Guid> SeedOwnedImagesAsync(
        PostgreSqlApiFactory factory,
        MonKadoUser owner,
        MonKadoUser other,
        byte[] profileBytes,
        byte[] wishBytes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var imageStore = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        var profileId = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();
        var member = await context.Users.SingleAsync(
            member => member.Id == owner.Id,
            TestContext.Current.CancellationToken);
        member.SetProfileImage(
            profileId,
            SHA256.HashData(profileBytes));
        var wishlist = new Wishlist(
            Guid.CreateVersion7(),
            owner.Id,
            "My wishlist",
            "MY WISHLIST",
            WishlistOccasion.Other,
            null,
            "My message");
        var wish = new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "My wish",
            "My note",
            "https://example.test/product",
            12.34m,
            1);
        wish.ReplaceImage(
            imageId,
            SHA256.HashData(wishBytes));
        var foreign = new Wishlist(
            Guid.CreateVersion7(),
            other.Id,
            "foreign-private-content",
            "FOREIGN-PRIVATE-CONTENT",
            WishlistOccasion.Other,
            null,
            null);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            wishlist.Id,
            other.Id);
        context.Wishlists.AddRange(
            wishlist,
            foreign);
        context.Wishes.Add(wish);
        context.WishlistParticipants.Add(participant);
        context.GiftReservations.Add(new GiftReservation(
                Guid.CreateVersion7(),
                wishlist.Id,
                wish.Id,
                participant.Id,
                1));
        var now = _clock
            .GetUtcNow()
            .UtcDateTime;
        context.AuthenticationSessions.Add(AuthenticationSession.Create(
                Guid.CreateVersion7(),
                owner.Id,
                SHA256.HashData(Encoding.UTF8.GetBytes("never-export-refresh-secret")),
                false,
                now,
                now.AddHours(8)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await imageStore.WritePendingAsync(
            profileId,
            profileBytes,
            TestContext.Current.CancellationToken);
        await imageStore.WritePendingAsync(
            imageId,
            wishBytes,
            TestContext.Current.CancellationToken);
        await imageStore.MarkCommittedAsync(
            profileId,
            TestContext.Current.CancellationToken);
        await imageStore.MarkCommittedAsync(
            imageId,
            TestContext.Current.CancellationToken);

        return wish.Id;
    }

    private static async Task AssertImageAsync(
        ZipArchive zip,
        string name,
        byte[] expected)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        await using var stream = entry.Open();
        var actual = new byte[expected.Length];
        await stream.ReadExactlyAsync(
            actual,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            expected,
            actual);
    }
}
