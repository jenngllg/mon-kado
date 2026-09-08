using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using SkiaSharp;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class ProfileImageIntegrationTests(PostgreSqlContainerFixture fixture) : IAsyncLifetime
{
    private const string ImageRoute = "/api/v1/members/current/profile/image";
    private readonly string _storagePath = Path.Combine(
        Path.GetTempPath(),
        "mon-kado-profile-image-tests",
        Guid
            .CreateVersion7()
            .ToString("N"));
    public ValueTask InitializeAsync()
    {

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {

        if (Directory.Exists(_storagePath))
            Directory.Delete(
                _storagePath,
                recursive: true);
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task UpsertAsync_WhenReplacingPhoto_PreservesIdempotenceAndInvalidatesPreviousUrl()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var initialTag = await GetEntityTagAsync(client);
        var source = CreatePng(SKColors.Purple);

        // Act
        using var first = await UploadAsync(
            client,
            source,
            initialTag);
        var firstUrl = await GetImageUrlAsync(first);
        using var repeated = await UploadAsync(
            client,
            source,
            first.Headers.ETag?.Tag);
        using var replacement = await UploadAsync(
            client,
            CreatePng(SKColors.Green),
            repeated.Headers.ETag?.Tag);
        var replacementUrl = await GetImageUrlAsync(replacement);
        using var anonymous = factory.CreateClient();
        using var obsoleteImage = await anonymous.GetAsync(
            firstUrl,
            TestContext.Current.CancellationToken);
        using var currentImage = await anonymous.GetAsync(
            replacementUrl,
            TestContext.Current.CancellationToken);
        var bytes = await currentImage.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users.SingleAsync(TestContext.Current.CancellationToken);
        var cleanup = scope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
        var deletion = await context.GiftImageDeletionOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            first.StatusCode);
        Assert.NotEqual(
            initialTag,
            first.Headers.ETag?.Tag);
        Assert.Equal(
            first.Headers.ETag?.Tag,
            repeated.Headers.ETag?.Tag);
        Assert.Equal(
            firstUrl,
            await GetImageUrlAsync(repeated));
        Assert.NotEqual(
            repeated.Headers.ETag?.Tag,
            replacement.Headers.ETag?.Tag);
        Assert.Equal(
            HttpStatusCode.NotFound,
            obsoleteImage.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            currentImage.StatusCode);
        Assert.Equal(
            "image/webp",
            currentImage.Content.Headers.ContentType?.MediaType);
        Assert.True(currentImage.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "nosniff",
            Assert.Single(currentImage.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(
            SHA256.HashData(bytes),
            storedMember.ProfileImageHash);
        Assert.NotEqual(
            source,
            bytes);
        Assert.NotNull(storedMember.UpdatedAt);
        Assert.True(await cleanup.IsReferencedAsync(
                storedMember.ProfileImageId.GetValueOrDefault(),
                TestContext.Current.CancellationToken));
        Assert.False(await cleanup.IsReferencedAsync(
                deletion.ImageId,
                TestContext.Current.CancellationToken));
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(
            512,
            bitmap.Width);
        Assert.Equal(
            256,
            bitmap.Height);
    }

    [Fact]
    public async Task DeleteAsync_WhenPhotoExists_UpdatesAllRepresentationsAndRejectsRepeatedDeletion()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        using var upload = await UploadAsync(
            client,
            CreatePng(SKColors.Blue),
            await GetEntityTagAsync(client));
        var imageUrl = await GetImageUrlAsync(upload);
        using var profileRequest = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/profile")
        {
            Content = JsonContent.Create(new { displayName = "Jennifer" })
        };
        profileRequest.Headers.TryAddWithoutValidation(
            "If-Match",
            upload.Headers.ETag?.Tag);
        using var profileResponse = await client.SendAsync(
            profileRequest,
            TestContext.Current.CancellationToken);
        var oldTag = profileResponse.Headers.ETag?.Tag;
        var sessionWithPhoto = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        var searchWithPhoto = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/members?displayName=Jen",
            TestContext.Current.CancellationToken);

        // Act
        using var deletion = await DeleteAsync(
            client,
            oldTag);
        using var staleDeletion = await DeleteAsync(
            client,
            oldTag);
        using var absentDeletion = await DeleteAsync(
            client,
            deletion.Headers.ETag?.Tag);
        using var oldImage = await client.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        var search = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/members?displayName=Jen",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            imageUrl,
            await GetImageUrlAsync(profileResponse));
        Assert.Equal(
            imageUrl,
            sessionWithPhoto
                .GetProperty("profileImageUrl")
                .GetString());
        Assert.Equal(
            imageUrl,
            Assert
                .Single(searchWithPhoto
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("profileImageUrl")
                .GetString());
        Assert.Equal(
            HttpStatusCode.NoContent,
            deletion.StatusCode);
        Assert.True(deletion.Headers.CacheControl?.NoStore);
        Assert.NotEqual(
            oldTag,
            deletion.Headers.ETag?.Tag);
        Assert.Empty(await deletion.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            staleDeletion.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            absentDeletion.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            oldImage.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            session
                .GetProperty("profileImageUrl")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            Assert
                .Single(search
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("profileImageUrl")
                .ValueKind);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(storedMember.ProfileImageId);
        Assert.Null(storedMember.ProfileImageHash);
        var deletionMessage = await context.GiftImageDeletionOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        var store = scope.ServiceProvider.GetRequiredService<IGiftImageStore>();
        await store.DeleteAsync(
            deletionMessage.ImageId,
            TestContext.Current.CancellationToken);
        await store.DeleteAsync(
            deletionMessage.ImageId,
            TestContext.Current.CancellationToken);
        Assert.Null(await store.OpenReadAsync(
                deletionMessage.ImageId,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertAsync_WhenCommitOutcomeIsAmbiguous_ConfirmsOnlyDurableChanges(bool commitSucceeds)
    {
        // Arrange
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var entityTag = await GetEntityTagAsync(client);

        if (commitSucceeds)
            interceptor.Arm();
        else
            interceptor.ArmBeforeCommit();

        // Act
        using var response = await UploadAsync(
            client,
            CreatePng(SKColors.Red),
            entityTag);
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            commitSucceeds ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            commitSucceeds ? JsonValueKind.String : JsonValueKind.Null,
            session
                .GetProperty("profileImageUrl")
                .ValueKind);
    }

    [Fact]
    public async Task UpsertAsync_WhenTwoRequestsUseSameVersion_OnlyOnePhotoIsCommitted()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var entityTag = await GetEntityTagAsync(client);

        // Act
        var responses = await Task.WhenAll(
            UploadAsync(
                client,
                CreatePng(SKColors.Red),
                entityTag),
            UploadAsync(
                client,
                CreatePng(SKColors.Green),
                entityTag));

        // Assert
        using var first = responses[0];
        using var second = responses[1];
        Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.GiftImageDeletionOutboxMessages.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.NotNull((await context.Users.SingleAsync(TestContext.Current.CancellationToken)).ProfileImageId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpsertAsync_WhenAnotherProfileMutationUsesSameVersion_OnlyOneMutationSucceeds(bool deletePhoto)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        using var original = await UploadAsync(
            client,
            CreatePng(SKColors.Blue),
            await GetEntityTagAsync(client));
        var originalUrl = await GetImageUrlAsync(original);
        var entityTag = original.Headers.ETag?.Tag;
        using var mutation = new HttpRequestMessage(
            deletePhoto ? HttpMethod.Delete : HttpMethod.Put,
            deletePhoto ? ImageRoute : "/api/v1/members/current/profile");

        if (!deletePhoto)
            mutation.Content = JsonContent.Create(new
            {
                displayName = "Updated profile"
            });
        mutation.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        // Act
        var responses = await Task.WhenAll(
            UploadAsync(
                client,
                CreatePng(SKColors.Red),
                entityTag),
            client.SendAsync(
                mutation,
                TestContext.Current.CancellationToken));

        // Assert
        using var upload = responses[0];
        using var other = responses[1];
        Assert.Single(
            responses,
            response => response.IsSuccessStatusCode);
        Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        using var oldPhoto = await client.GetAsync(
            originalUrl,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            deletePhoto || upload.IsSuccessStatusCode ? HttpStatusCode.NotFound : HttpStatusCode.OK,
            oldPhoto.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var outboxCount = await context.GiftImageDeletionOutboxMessages.CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            deletePhoto || upload.IsSuccessStatusCode ? 1 : 0,
            outboxCount);
    }

    [Fact]
    public async Task ConfirmAccountDeletionAsync_WhenMemberHasPhoto_InvalidatesUrlAndQueuesItsFile()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        using var upload = await UploadAsync(
            client,
            CreatePng(SKColors.Blue),
            await GetEntityTagAsync(client));
        var imageUrl = await GetImageUrlAsync(upload);
        using var requestResponse = await client.PostAsync(
            "/api/v1/members/current/deletion-requests",
            null,
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.Accepted,
            requestResponse.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var deletionRequest = await context.MemberAccountDeletionRequests.SingleAsync(TestContext.Current.CancellationToken);
        var token = scope.ServiceProvider
            .GetRequiredService<IMemberAccountDeletionTokenService>()
            .Create(
            member.Id,
            deletionRequest.Id);

        // Act
        using var deletion = await client.PostAsJsonAsync(
            "/api/v1/members/current/deletion-requests/confirm",
            new
            {
                token
            },
            TestContext.Current.CancellationToken);
        using var anonymous = factory.CreateClient();
        using var obsoleteImage = await anonymous.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            deletion.StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            obsoleteImage.StatusCode);
        var message = await context.GiftImageDeletionOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        var cleanup = scope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
        Assert.False(await cleanup.IsReferencedAsync(
                message.ImageId,
                TestContext.Current.CancellationToken));
        Assert.False(await context.Users.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteAsync_WhenCommitOutcomeIsAmbiguous_ConfirmsOnlyDurableRemoval(bool commitSucceeds)
    {
        // Arrange
        var interceptor = new GiftImageCommitInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        using var upload = await UploadAsync(
            client,
            CreatePng(SKColors.Blue),
            await GetEntityTagAsync(client));
        var imageUrl = await GetImageUrlAsync(upload);

        if (commitSucceeds)
            interceptor.Arm();
        else
            interceptor.ArmBeforeCommit();

        // Act
        using var response = await DeleteAsync(
            client,
            upload.Headers.ETag?.Tag);
        using var image = await client.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            commitSucceeds ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            commitSucceeds ? HttpStatusCode.NotFound : HttpStatusCode.OK,
            image.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            commitSucceeds ? 1 : 0,
            await context.GiftImageDeletionOutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("concurrency", 412)]
    [InlineData("unavailable", 503)]
    [InlineData("unexpected", 500)]
    public async Task UpsertAsync_WhenSaveFails_RollsBackPhotoAndDeletionOutbox(
        string scenario,
        int expectedStatus)
    {
        // Arrange
        var interceptor = new ProfileImageSaveFailureInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        using var original = await UploadAsync(
            client,
            CreatePng(SKColors.Red),
            await GetEntityTagAsync(client));
        var originalUrl = await GetImageUrlAsync(original);
        interceptor.Arm(scenario switch
        {
            "concurrency" => new DbUpdateConcurrencyException(),
            "unavailable" => new TimeoutException(),
            _ => new InvalidOperationException("Injected save failure.")
        });

        // Act
        using var response = await UploadAsync(
            client,
            CreatePng(SKColors.Green),
            original.Headers.ETag?.Tag);
        using var photo = await client.GetAsync(
            originalUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            (int)response.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            photo.StatusCode);
        Assert.Equal(
            original.Headers.ETag?.Tag,
            await GetEntityTagAsync(client));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.GiftImageDeletionOutboxMessages.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertAsync_WhenAccountIsMissingOrUnconfirmed_RejectsAtPersistenceBoundary(bool accountExists)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var memberId = Guid.CreateVersion7();

        if (accountExists)
        {
            var member = await CreateMemberAsync(factory);
            memberId = member.Id;
            await using var updateScope = factory.Services.CreateAsyncScope();
            var updateContext = updateScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await updateContext.Users.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.EmailConfirmed,
                    false),
                TestContext.Current.CancellationToken);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IProfileImageService>();

        // Act
        var action = () => service.UpsertAsync(
            memberId,
            Guid.CreateVersion7(),
            new byte[32],
            42,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<JennGllg.Fr.MonKado.Back.Application.Common.Exceptions.InvalidAuthenticationSessionException>(action);
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlBecomesUnavailable_DoesNotServeTheStoredPhoto()
    {
        // Arrange
        var failure = new AccountDeletionVerificationFailure();
        await using var factory = await CreateFactoryAsync(failure);
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        using var upload = await UploadAsync(
            client,
            CreatePng(SKColors.Blue),
            await GetEntityTagAsync(client));
        var imageUrl = await GetImageUrlAsync(upload);
        using var anonymousClient = factory.CreateClient();
        failure.Unavailable = true;

        // Act
        using var response = await anonymousClient.GetAsync(
            imageUrl,
            TestContext.Current.CancellationToken);
        failure.Unavailable = false;

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
    }

    [Fact]
    public async Task UpsertAsync_WhenCommitVerificationIsUnavailable_DoesNotClaimSuccess()
    {
        // Arrange
        var failure = new AccountDeletionVerificationFailure();
        var interceptor = new AccountDeletionLostCommitInterceptor(failure);
        await using var factory = await CreateFactoryAsync(
            interceptor,
            failure);
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var entityTag = await GetEntityTagAsync(client);
        interceptor.Armed = true;

        // Act
        using var response = await UploadAsync(
            client,
            CreatePng(SKColors.Blue),
            entityTag);
        failure.Unavailable = false;

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            JsonValueKind.String,
            session
                .GetProperty("profileImageUrl")
                .ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertAsync_WhenAccountBecomesInaccessibleAfterCommit_Returns401(bool accountDeleted)
    {
        // Arrange
        var interceptor = new ProfileImageCommitAccountChangeInterceptor();
        await using var factory = await CreateFactoryAsync(interceptor);
        var member = await CreateMemberAsync(factory);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            member.Id,
            TestContext.Current.CancellationToken);
        var entityTag = await GetEntityTagAsync(client);
        interceptor.AfterCommit = async cancellationToken =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var members = context.Users.Where(candidate => candidate.Id == member.Id);

            if (accountDeleted)
            {
                await members.ExecuteDeleteAsync(cancellationToken);

                return;
            }

            await members.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.EmailConfirmed,
                    false),
                cancellationToken);
        };

        // Act
        using var response = await UploadAsync(
            client,
            CreatePng(SKColors.Blue),
            entityTag);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(params IInterceptor[] interceptors)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services =>
            {

                if (interceptors.Length > 0)
                    services.ConfigureDbContext<MonKadoDbContext>((
                            _,
                            options) => options.AddInterceptors(interceptors));
            },
            giftImageStoragePath: _storagePath);
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<MonKadoUser> CreateMemberAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            Email = "profile-image@example.test",
            UserName = "profile-image@example.test",
            DisplayName = "Jennifer",
            EmailConfirmed = true
        };
        Assert.True((await manager.CreateAsync(member)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(
                member,
                RoleNames.Member)).Succeeded);

        return member;
    }



    private static async Task<string> GetEntityTagAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        return Assert.IsType<string>(response.Headers.ETag?.Tag);
    }

    private static async Task<string> GetImageUrlAsync(HttpResponseMessage response)
    {
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        return Assert.IsType<string>(body
                .GetProperty("profileImageUrl")
                .GetString());
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[] content,
        string? entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            ImageRoute);
        var multipart = new MultipartFormDataContent();
        multipart.Add(
            new ByteArrayContent(content),
            "image",
            "untrusted.bin");
        request.Content = multipart;
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(
        HttpClient client,
        string? entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            ImageRoute);
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static byte[] CreatePng(SKColor color)
    {
        using var bitmap = new SKBitmap(
            1024,
            512);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Png,
            100);

        return data.ToArray();
    }
}
