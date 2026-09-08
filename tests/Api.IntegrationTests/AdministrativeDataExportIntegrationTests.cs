using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class AdministrativeDataExportIntegrationTests(PostgreSqlContainerFixture fixture) : IAsyncLifetime
{
    private readonly string _storagePath = Path.Combine(
        Path.GetTempPath(),
        $"monkado-admin-export-tests-{Guid.CreateVersion7():N}");
    private readonly MutableTimeProvider _clock = new(DateTimeOffset.UtcNow);
    public ValueTask InitializeAsync()
    {

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        var path = Path.GetFullPath(_storagePath);

        if (Path.GetDirectoryName(path) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()))
            throw new InvalidOperationException("Unexpected test storage directory.");

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
    public async Task RequestAsync_WhenMemberExists_GeneratesSharedArchiveWithConditionalNotification(bool confirmed)
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
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider
                .GetRequiredService<MonKadoDbContext>()
                .Users
                .Where(member => member.Id == memberId)
                .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.EmailConfirmed,
                    confirmed),
                cancellationToken);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            memberId);
        var route = GetRoute(memberId);

        // Act
        using var requested = await admin.PostAsJsonAsync(
            route,
            new
            {
                requestReference = "  SUPPORT-807  "
            },
            cancellationToken);
        var requestBody = await requested.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var exportId = requestBody
            .GetProperty("id")
            .GetGuid();
        using var repeat = await admin.PostAsJsonAsync(
            route,
            new
            {
                requestReference = "SUPPORT-808"
            },
            cancellationToken);
        await GenerateAsync(factory);
        using var ready = await admin.GetAsync(
            $"{route}/{exportId:D}",
            cancellationToken);
        using var latest = await admin.GetAsync(
            $"{route}/latest",
            cancellationToken);
        using var archiveResponse = await admin.GetAsync(
            $"{route}/{exportId:D}/archive",
            cancellationToken);
        using var personalResponse = await owner.GetAsync(
            $"/api/v1/members/current/data-exports/{exportId:D}/archive",
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            requested.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            repeat.StatusCode);
        Assert.Equal(
            $"{route}/{exportId:D}",
            requested.Headers.Location?.OriginalString);
        Assert.Equal(
            HttpStatusCode.OK,
            ready.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            latest.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            archiveResponse.StatusCode);
        Assert.Equal(
            confirmed ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
            personalResponse.StatusCode);
        Assert.True(archiveResponse.Headers.CacheControl?.NoStore);
        using var zip = new ZipArchive(await archiveResponse.Content.ReadAsStreamAsync(cancellationToken));
        var entry = zip.GetEntry("data.json");
        Assert.NotNull(entry);
        await using var stream = entry.Open();
        using var data = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        Assert.Contains(
            memberId.ToString(),
            data.RootElement.GetRawText());
        Assert.DoesNotContain(
            administratorId.ToString(),
            data.RootElement.GetRawText());
        await using var verification = factory.Services.CreateAsyncScope();
        var context = verification.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await context.MemberDataExports.CountAsync(cancellationToken));
        Assert.Equal(
            confirmed ? 1 : 0,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.MemberDataExportId == exportId,
                cancellationToken));
        var events = await context.AdministrativeDataExportEvents.ToArrayAsync(cancellationToken);
        Assert.Equal(
            2,
            events.Count(audit => audit.Action == AdministrativeDataExportAction.Requested));
        var download = Assert.Single(
            events,
            audit => audit.Action == AdministrativeDataExportAction.DownloadStarted);
        Assert.Equal(
            administratorId,
            download.AdministratorId);
        Assert.Equal(
            memberId,
            download.MemberId);
        Assert.All(
            events,
            audit => Assert.Equal(
                exportId,
                audit.ExportId));
    }

    [Fact]
    public async Task RequestAsync_WhenPersonalArchiveExists_RequiresAdministrativeRequestBeforeReuse()
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
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            memberId);
        using var personalRequest = await owner.PostAsync(
            "/api/v1/members/current/data-exports",
            null,
            cancellationToken);
        var body = await personalRequest.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var exportId = body
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);
        var route = GetRoute(memberId);

        // Act
        using var before = await admin.GetAsync(
            $"{route}/{exportId:D}/archive",
            cancellationToken);
        using var request = await admin.PostAsJsonAsync(
            route,
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);
        var requested = await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        using var after = await admin.GetAsync(
            $"{route}/{exportId:D}/archive",
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            before.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            request.StatusCode);
        Assert.Equal(
            exportId,
            requested
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            HttpStatusCode.OK,
            after.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequestAsync_WhenCommitOutcomeIsAmbiguous_ConfirmsOnlyDurableRequestAndAudit(bool beforeCommit)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);

        if (beforeCommit)
            interceptor.ArmBeforeCommit();
        else
            interceptor.Arm();

        // Act
        using var response = await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            beforeCommit ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            beforeCommit ? 0 : 1,
            await context.MemberDataExports.CountAsync(cancellationToken));
        Assert.Equal(
            beforeCommit ? 0 : 1,
            await context.AdministrativeDataExportEvents.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task CleanupAsync_WhenAccountAndArchiveAreDeleted_RetainsAuditUntilSixCalendarMonths()
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
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var response = await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var exportId = body
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();

        // Act
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteDeleteAsync(cancellationToken);
        using var inaccessible = await admin.GetAsync(
            $"{GetRoute(memberId)}/{exportId:D}/archive",
            cancellationToken);
        await jobs.CleanupAsync(cancellationToken);
        var retained = await context.AdministrativeDataExportEvents
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var archivesAfterDeletion = await context.MemberDataExports.CountAsync(cancellationToken);
        var expiration = retained.CreatedAt.AddMonths(6);
        _clock.Advance(expiration - _clock
                .GetUtcNow()
                .UtcDateTime - TimeSpan.FromSeconds(1));
        await jobs.CleanupAsync(cancellationToken);
        var beforeExpiration = await context.AdministrativeDataExportEvents.CountAsync(cancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await jobs.CleanupAsync(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            inaccessible.StatusCode);
        Assert.Equal(
            0,
            archivesAfterDeletion);
        Assert.Null(retained.MemberId);
        Assert.Equal(
            administratorId,
            retained.AdministratorId);
        Assert.Equal(
            1,
            beforeExpiration);
        Assert.Empty(await context.AdministrativeDataExportEvents.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadAsync_WhenAuditCommitIsAmbiguous_ReleasesOnlyAfterDurableAudit(bool beforeCommit)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var request = await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);
        var body = await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var exportId = body
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);

        if (beforeCommit)
            interceptor.ArmBeforeCommit();
        else
            interceptor.Arm();

        // Act
        using var response = await admin.GetAsync(
            $"{GetRoute(memberId)}/{exportId:D}/archive",
            cancellationToken);

        // Assert
        Assert.Equal(
            beforeCommit ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            !beforeCommit,
            response.Content.Headers.ContentType?.MediaType == "application/zip");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            beforeCommit ? 0 : 1,
            await context.AdministrativeDataExportEvents.CountAsync(
                audit => audit.Action == AdministrativeDataExportAction.DownloadStarted,
                cancellationToken));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("missing")]
    [InlineData("unconfirmed")]
    [InlineData("targetMissing")]
    public async Task GetAsync_WhenCurrentAccessIsInvalid_RejectsDirectServiceAccess(string scenario)
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

        if (scenario is "unconfirmed")
            await context.Users
                .Where(user => user.Id == administratorId)
                .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.EmailConfirmed,
                    false),
                cancellationToken);
        var actor = scenario switch
        {
            "member" => memberId,
            "missing" => Guid.CreateVersion7(),
            _ => administratorId
        };
        var target = scenario is "targetMissing" ? Guid.CreateVersion7() : memberId;
        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeDataExportService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.GetAsync(
                actor,
                target,
                null,
                cancellationToken));

        // Assert
        switch (scenario)
        {
            case "member":
                Assert.IsType<AdministratorAccessDeniedException>(exception);
                break;
            case "targetMissing":
                Assert.IsType<PersonalDataExportNotFoundException>(exception);
                break;
            default:
                Assert.IsType<InvalidAuthenticationSessionException>(exception);
                break;
        }

        Assert.Empty(await context.AdministrativeDataExportEvents.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("read")]
    [InlineData("download")]
    public async Task Service_WhenDatabaseIsUnavailable_ReturnsStructuredDependencyFailure(string operation)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var outage = new AccountDeletionVerificationFailure();
        await using var factory = await CreateFactoryAsync(outage);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeDataExportService>();
        outage.Unavailable = true;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            {
                switch (operation)
                {
                    case "request":
                        await service.RequestAsync(
                            administratorId,
                            memberId,
                            "SUPPORT-807",
                            cancellationToken);
                        break;
                    case "read":
                        await service.GetAsync(
                            administratorId,
                            memberId,
                            null,
                            cancellationToken);
                        break;
                    default:
                        await service.OpenArchiveAsync(
                            administratorId,
                            memberId,
                            Guid.CreateVersion7(),
                            cancellationToken);
                        break;
                }
            });
        outage.Unavailable = false;

        // Assert
        Assert.IsType<DependencyUnavailableException>(exception);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("outage")]
    public async Task OpenArchiveAsync_WhenStateChangesDuringStorageOpen_RechecksBeforeReleasingStream(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var outage = new AccountDeletionVerificationFailure();
        await using var factory = await CreateFactoryAsync(outage);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeDataExportService>();
        var requested = await service.RequestAsync(
            administratorId,
            memberId,
            "SUPPORT-807",
            cancellationToken);
        await GenerateAsync(factory);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var export = await context.MemberDataExports
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var streamMock = new Mock<Stream>(MockBehavior.Strict);
        streamMock
            .SetupGet(stream => stream.Length)
            .Returns(export.SizeInBytes.GetValueOrDefault());
        streamMock
            .Setup(stream => stream.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        var storeMock = new Mock<IPersonalDataExportStore>(MockBehavior.Strict);
        storeMock
            .Setup(store => store.OpenReadAsync(
                requested.Id,
                export.ArchiveId.GetValueOrDefault(),
                cancellationToken))
            .Returns(async () =>
            {

                if (scenario is "expired")
                    _clock.Advance(TimeSpan.FromHours(24));

                if (scenario is "revoked")
                    await context.UserRoles
                        .Where(role => role.UserId == administratorId)
                        .ExecuteDeleteAsync(cancellationToken);
                outage.Unavailable = scenario is "outage";

                return streamMock.Object;
            });
        var reader = new PersonalDataExportArchiveReader(
            storeMock.Object,
            _clock);
        var administrativeService = new AdministrativeDataExportService(
            context,
            context,
            scope.ServiceProvider.GetRequiredService<IPersonalDataExportRequestRepository>(),
            reader,
            scope.ServiceProvider.GetRequiredService<IAdministratorAccessService>(),
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _clock);

        // Act
        var exception = await Record.ExceptionAsync(() => administrativeService.OpenArchiveAsync(
                administratorId,
                memberId,
                requested.Id,
                cancellationToken));
        outage.Unavailable = false;

        // Assert
        switch (scenario)
        {
            case "expired":
                Assert.IsType<PersonalDataExportNotFoundException>(exception);
                break;
            case "revoked":
                Assert.IsType<AdministratorAccessDeniedException>(exception);
                break;
            default:
                Assert.IsType<DependencyUnavailableException>(exception);
                break;
        }

        Assert.False(await context.AdministrativeDataExportEvents.AnyAsync(
                audit => audit.Action == AdministrativeDataExportAction.DownloadStarted,
                cancellationToken));
        storeMock.Verify(
            store => store.OpenReadAsync(
                requested.Id,
                export.ArchiveId.GetValueOrDefault(),
                cancellationToken),
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_WhenVerificationIsUnavailable_FailsClosedWithoutDuplicatingAudit(bool download)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var outage = new AccountDeletionVerificationFailure();
        var interceptor = new AccountDeletionLostCommitInterceptor(outage);
        await using var factory = await CreateFactoryAsync(
            outage,
            interceptor);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var requested = await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);
        var body = await requested.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var exportId = body
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);
        interceptor.Armed = true;

        // Act
        using var response = download ? await admin.GetAsync(
            $"{GetRoute(memberId)}/{exportId:D}/archive",
            cancellationToken) : await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-808"
            },
            cancellationToken);
        outage.Unavailable = false;

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.False(response.Content.Headers.ContentType?.MediaType == "application/zip");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            1,
            await context.MemberDataExports.CountAsync(cancellationToken));
        Assert.Equal(
            2,
            await context.AdministrativeDataExportEvents.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("actorDeleted", HttpStatusCode.Unauthorized)]
    [InlineData("targetDeleted", HttpStatusCode.NotFound)]
    [InlineData("expired", HttpStatusCode.ServiceUnavailable)]
    public async Task DownloadAsync_WhenStateChangesAfterAmbiguousCommit_RechecksCurrentAccess(
        string scenario,
        HttpStatusCode expected)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new ProfileImageCommitAccountChangeInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var requested = await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);
        var body = await requested.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var exportId = body
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);
        interceptor.AfterCommit = async token =>
        {

            if (scenario is "expired")
            {
                _clock.Advance(TimeSpan.FromHours(24));

                return;
            }

            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var deletedId = scenario is "actorDeleted" ? administratorId : memberId;
            await context.Users
                .Where(user => user.Id == deletedId)
                .ExecuteDeleteAsync(token);
        };

        // Act
        using var response = await admin.GetAsync(
            $"{GetRoute(memberId)}/{exportId:D}/archive",
            cancellationToken);

        // Assert
        Assert.Equal(
            expected,
            response.StatusCode);
        Assert.False(response.Content.Headers.ContentType?.MediaType == "application/zip");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishAsync_WhenUnconfirmedAccountIsExported_NotifiesOnlyIfConfirmedAtPublication(bool confirmBeforePublication)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(user => user.Id == memberId)
            .ExecuteUpdateAsync(
            setters => setters.SetProperty(
                user => user.EmailConfirmed,
                false),
            cancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeDataExportService>();
        var request = await service.RequestAsync(
            administratorId,
            memberId,
            "SUPPORT-807",
            cancellationToken);
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var work = await jobs.ClaimAsync(cancellationToken);
        Assert.NotNull(work);
        var archive = await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            work,
            cancellationToken);

        if (confirmBeforePublication)
            await context.Users
                .Where(user => user.Id == memberId)
                .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.EmailConfirmed,
                    true),
                cancellationToken);
        interceptor.Arm();

        // Act
        var published = await jobs.CompleteAsync(
            work,
            archive,
            cancellationToken);
        await context.Users
            .Where(user => user.Id == memberId)
            .ExecuteUpdateAsync(
            setters => setters.SetProperty(
                user => user.EmailConfirmed,
                true),
            cancellationToken);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            memberId);
        using var download = await owner.GetAsync(
            $"/api/v1/members/current/data-exports/{request.Id:D}/archive",
            cancellationToken);

        // Assert
        Assert.True(published);
        Assert.Equal(
            HttpStatusCode.OK,
            download.StatusCode);
        Assert.Equal(
            confirmBeforePublication ? 1 : 0,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.MemberDataExportId == request.Id,
                cancellationToken));
    }

    [Fact]
    public async Task RequestAsync_WhenMemberAndAdministratorRace_SharesOneSlotAndRollingQuota()
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
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var owner = ReportedWishlistTestData.CreateClient(
            factory,
            memberId);

        // Act
        var responses = await Task.WhenAll(
            admin.PostAsJsonAsync(
                GetRoute(memberId),
                new
                {
                    requestReference = "SUPPORT-807"
                },
                cancellationToken),
            owner.PostAsync(
                "/api/v1/members/current/data-exports",
                null,
                cancellationToken));
        using var administrative = responses[0];
        using var personal = responses[1];
        var adminBody = await administrative.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var personalBody = await personal.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        for (var index = 0; index < 3; index++)
        {

            if (index > 0)
            {
                using var next = await admin.PostAsJsonAsync(
                    GetRoute(memberId),
                    new
                    {
                        requestReference = "SUPPORT-807"
                    },
                    cancellationToken);
                Assert.Equal(
                    HttpStatusCode.Accepted,
                    next.StatusCode);
            }

            var work = await jobs.ClaimAsync(cancellationToken);
            Assert.NotNull(work);
            await jobs.FailAsync(
                work,
                PersonalDataExportFailure.TooLarge,
                cancellationToken);
        }

        using var fourthAdmin = await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);
        using var fourthMember = await owner.PostAsync(
            "/api/v1/members/current/data-exports",
            null,
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            administrative.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            personal.StatusCode);
        Assert.Equal(
            adminBody
                .GetProperty("id")
                .GetGuid(),
            personalBody
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            fourthAdmin.StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            fourthMember.StatusCode);
        Assert.Equal(
            3,
            await context.MemberDataExports.CountAsync(cancellationToken));
        Assert.Equal(
            3,
            await context.AdministrativeDataExportEvents.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData("request", false)]
    [InlineData("latest", false)]
    [InlineData("details", false)]
    [InlineData("archive", false)]
    [InlineData("request", true)]
    [InlineData("latest", true)]
    [InlineData("details", true)]
    [InlineData("archive", true)]
    public async Task Endpoint_WhenCallerIsNotAdministrator_RejectsEveryRoute(
        string operation,
        bool anonymous)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = anonymous
            ? factory.CreateClient()
            : ReportedWishlistTestData.CreateClient(
                factory,
                memberId);
        var route = GetRoute(memberId);
        var exportId = Guid.CreateVersion7();
        var path = operation switch
        {
            "latest" => $"{route}/latest",
            "details" => $"{route}/{exportId:D}",
            "archive" => $"{route}/{exportId:D}/archive",
            _ => route
        };

        // Act
        using var response = operation is "request"
            ? await client.PostAsJsonAsync(
                path,
                new
                {
                    requestReference = "SUPPORT-807"
                },
                cancellationToken)
            : await client.GetAsync(
                path,
                cancellationToken);

        // Assert
        Assert.Equal(
            anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task GetAsync_WhenExportBelongsToAnotherTarget_ReturnsNotFound()
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
        using var admin = ReportedWishlistTestData.CreateClient(
            factory,
            administratorId);
        using var requested = await admin.PostAsJsonAsync(
            GetRoute(memberId),
            new
            {
                requestReference = "SUPPORT-807"
            },
            cancellationToken);
        var body = await requested.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var exportId = body
            .GetProperty("id")
            .GetGuid();
        await GenerateAsync(factory);

        // Act
        using var details = await admin.GetAsync(
            $"{GetRoute(administratorId)}/{exportId:D}",
            cancellationToken);
        using var archive = await admin.GetAsync(
            $"{GetRoute(administratorId)}/{exportId:D}/archive",
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            details.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            archive.StatusCode);
    }

    [Fact]
    public async Task CleanupAsync_WhenMonthEndRetentionExpires_PurgesBoundedIdempotentBatches()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var batchSize = scope.ServiceProvider
            .GetRequiredService<IOptions<PersonalDataExportOptions>>()
            .Value.CleanupBatchSize;
        var createdAt = new DateTime(
            2026,
            8,
            31,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var events = Enumerable.Range(
                0,
                batchSize + 1)
            .Select(_ => new AdministrativeDataExportEvent(
                null,
                null,
                Guid.CreateVersion7(),
                AdministrativeDataExportAction.Requested,
                "SUPPORT-807",
                createdAt));
        context.AdministrativeDataExportEvents.AddRange(events);
        await context.SaveChangesAsync(cancellationToken);
        var deadline = createdAt.AddMonths(6);
        var now = _clock
            .GetUtcNow()
            .UtcDateTime;
        _clock.Advance(deadline - now - TimeSpan.FromSeconds(1));

        // Act
        await jobs.CleanupAsync(cancellationToken);
        var beforeDeadline = await context.AdministrativeDataExportEvents.CountAsync(cancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await jobs.CleanupAsync(cancellationToken);
        var afterFirstBatch = await context.AdministrativeDataExportEvents.CountAsync(cancellationToken);
        await jobs.CleanupAsync(cancellationToken);
        await jobs.CleanupAsync(cancellationToken);

        // Assert
        Assert.Equal(
            batchSize + 1,
            beforeDeadline);
        Assert.Equal(
            1,
            afterFirstBatch);
        Assert.Empty(await context.AdministrativeDataExportEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task RequestAsync_WhenOnlyAdministratorRefreshCookieIsPresent_RejectsWithoutExportOrAudit()
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
        await scope.ServiceProvider
            .GetRequiredService<IUnitOfWork>()
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
                requestReference = "SUPPORT-807"
            },
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.MemberDataExports.ToArrayAsync(cancellationToken));
        Assert.Empty(await context.AdministrativeDataExportEvents.ToArrayAsync(cancellationToken));
        Assert.NotNull(await sessions.ProveCurrentSessionAsync(
            session.RefreshToken,
            cancellationToken));
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

    private static async Task GenerateAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        var work = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(work);
        var archive = await scope.ServiceProvider
            .GetRequiredService<IPersonalDataExportArchiveBuilder>()
            .BuildAsync(
            work,
            TestContext.Current.CancellationToken);
        Assert.True(await jobs.CompleteAsync(
                work,
                archive,
                TestContext.Current.CancellationToken));
    }

    private static string GetRoute(Guid memberId)
    {

        return $"/api/v1/admin/members/{memberId:D}/data-exports";
    }
}
