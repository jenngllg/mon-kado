using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset _referenceTime = new(
        2026,
        8,
        25,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task GetAllAsync_WhenMemberOwnsWishlists_ReturnsOnlyOwnedWishlistsInReverseCreationOrder()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        var otherOwner = await CreateMemberAsync(
            factory,
            "other@example.fr");
        using var ownerClient = CreateAuthorizedClient(
            factory,
            owner.Id);
        var pastWishlist = await SeedWishlistAsync(
            factory,
            Guid.CreateVersion7(),
            owner.Id,
            "Liste passée",
            new DateOnly(
                2020,
                1,
                1));
        timeProvider.UtcNow = _referenceTime.AddMinutes(1);
        await SeedWishlistAsync(
            factory,
            Guid.CreateVersion7(),
            otherOwner.Id,
            "Liste étrangère",
            null);
        timeProvider.UtcNow = _referenceTime.AddMinutes(2);
        var undatedWishlist = await SeedWishlistAsync(
            factory,
            Guid.CreateVersion7(),
            owner.Id,
            "Liste sans date",
            null);

        // Act
        using var response = await ownerClient.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);
        var wishlists = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var items = wishlists.EnumerateArray().ToArray();

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            2,
            items.Length);
        Assert.Equal(
            undatedWishlist.Id,
            items[0].GetProperty("id").GetGuid());
        Assert.Equal(
            "Liste sans date",
            items[0].GetProperty("name").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            items[0].GetProperty("eventDate").ValueKind);
        Assert.Equal(
            pastWishlist.Id,
            items[1].GetProperty("id").GetGuid());
        Assert.Equal(
            "2020-01-01",
            items[1].GetProperty("eventDate").GetString());
    }

    [Fact]
    public async Task GetAllAsync_WhenCreationDatesAreEqual_OrdersByDescendingIdentifier()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await SeedWishlistAsync(
            factory,
            lowerId,
            owner.Id,
            "Première liste",
            null);
        await SeedWishlistAsync(
            factory,
            higherId,
            owner.Id,
            "Deuxième liste",
            null);
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);
        var wishlists = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var identifiers = wishlists
            .EnumerateArray()
            .Select(wishlist => wishlist.GetProperty("id").GetGuid())
            .ToArray();

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            [
                higherId,
                lowerId
            ],
            identifiers);
    }

    [Fact]
    public async Task GetAllAsync_WhenMemberOwnsNoWishlist_ReturnsEmptyCollection()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);
        var wishlists = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Empty(wishlists.EnumerateArray());
    }

    [Fact]
    public async Task GetAllAsync_WhenAuthenticatedMemberWasDeleted_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        await DeleteMemberAsync(
            factory,
            owner.Id);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/wishlists",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenRequestIsValid_PersistsAndReturnsPrivateWishlist()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);

        // Act
        using var creationResponse = await CreateWishlistAsync(
            client,
            "  Liste de Le\u0301a  ");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishlistId = created.GetProperty("id").GetGuid();
        using var retrievalResponse = await client.GetAsync(
            creationResponse.Headers.Location,
            TestContext.Current.CancellationToken);
        var retrieved = await retrievalResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var stored = await GetWishlistAsync(
            factory,
            wishlistId);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            creationResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            retrievalResponse.StatusCode);
        Assert.Equal(
            wishlistId,
            retrieved.GetProperty("id").GetGuid());
        Assert.Equal(
            owner.Id,
            stored.OwnerId);
        Assert.Equal(
            7,
            stored.Id.Version);
        Assert.Equal(
            "Liste de Léa",
            stored.Name);
        Assert.Equal(
            "LISTE DE LÉA",
            stored.NormalizedName);
        Assert.Equal(
            WishlistOccasion.Birthday,
            stored.Occasion);
        Assert.Equal(
            new DateOnly(
                2099,
                9,
                24),
            stored.EventDate);
        Assert.Equal(
            "Merci d’être là",
            stored.Message);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            stored.CreatedAt);
        Assert.Null(stored.UpdatedAt);
        Assert.NotEqual(
            0u,
            stored.Version);
        Assert.Equal(
            creationResponse.Headers.ETag?.Tag,
            retrievalResponse.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task CreateAsync_WhenNormalizedNameAlreadyExistsForOwner_ReturnsConflict()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        var otherOwner = await CreateMemberAsync(
            factory,
            "other@example.fr");
        using var ownerClient = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var otherOwnerClient = CreateAuthorizedClient(
            factory,
            otherOwner.Id);
        using var firstResponse = await CreateWishlistAsync(
            ownerClient,
            "Liste de Le\u0301a");

        // Act
        using var duplicateResponse = await CreateWishlistAsync(
            ownerClient,
            "  LISTE DE LÉA  ");
        using var otherOwnerResponse = await CreateWishlistAsync(
            otherOwnerClient,
            "liste de léa");

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            firstResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            duplicateResponse.StatusCode);
        var error = await duplicateResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNameAlreadyExists,
            error.ErrorCode);
        Assert.Equal(
            HttpStatusCode.Created,
            otherOwnerResponse.StatusCode);
        Assert.Equal(
            2,
            await CountWishlistsAsync(factory));
    }

    [Fact]
    public async Task GetAsync_WhenWishlistBelongsToAnotherMember_ReturnsNotFound()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        var otherMember = await CreateMemberAsync(
            factory,
            "other@example.fr");
        using var ownerClient = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var otherClient = CreateAuthorizedClient(
            factory,
            otherMember.Id);
        using var creationResponse = await CreateWishlistAsync(
            ownerClient,
            "Liste privée");

        // Act
        using var response = await otherClient.GetAsync(
            creationResponse.Headers.Location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNotFound,
            error.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenUsingEntityTags_UpdatesThenRejectsStaleVersionAndPreservesNoOp()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishlistAsync(
            client,
            "Liste initiale");
        var wishlistId = GetWishlistId(creationResponse);
        var initialEntityTag = creationResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The created wishlist ETag is missing.");
        timeProvider.UtcNow = _referenceTime.AddMinutes(1);

        // Act
        using var updateResponse = await UpdateWishlistAsync(
            client,
            wishlistId,
            initialEntityTag,
            "  Liste modifiée  ",
            "wedding",
            "2099-12-24",
            "  Nouveau message  ");
        var updatedEntityTag = updateResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The updated wishlist ETag is missing.");
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var storedAfterUpdate = await GetWishlistAsync(
            factory,
            wishlistId);
        timeProvider.UtcNow = _referenceTime.AddMinutes(2);
        using var unchangedResponse = await UpdateWishlistAsync(
            client,
            wishlistId,
            updatedEntityTag,
            "  Liste modifiée  ",
            "wedding",
            "2099-12-24",
            "  Nouveau message  ");
        var storedAfterNoOp = await GetWishlistAsync(
            factory,
            wishlistId);
        timeProvider.UtcNow = _referenceTime.AddMinutes(3);
        using var clearedResponse = await UpdateWishlistAsync(
            client,
            wishlistId,
            updatedEntityTag,
            "Liste modifiée",
            "wedding",
            null,
            null);
        var clearedEntityTag = clearedResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The cleared wishlist ETag is missing.");
        var storedAfterClear = await GetWishlistAsync(
            factory,
            wishlistId);
        using var staleResponse = await UpdateWishlistAsync(
            client,
            wishlistId,
            initialEntityTag,
            "Autre nom",
            "other",
            null,
            null);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            updateResponse.StatusCode);
        Assert.NotEqual(
            initialEntityTag,
            updatedEntityTag);
        Assert.Equal(
            wishlistId,
            updated.GetProperty("id").GetGuid());
        Assert.Equal(
            "Liste modifiée",
            updated.GetProperty("name").GetString());
        Assert.Equal(
            "LISTE MODIFIÉE",
            storedAfterUpdate.NormalizedName);
        Assert.Equal(
            WishlistOccasion.Wedding,
            storedAfterUpdate.Occasion);
        Assert.Equal(
            new DateOnly(
                2099,
                12,
                24),
            storedAfterUpdate.EventDate);
        Assert.Equal(
            "Nouveau message",
            storedAfterUpdate.Message);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            storedAfterUpdate.CreatedAt);
        Assert.Equal(
            _referenceTime.AddMinutes(1).UtcDateTime,
            storedAfterUpdate.UpdatedAt);
        Assert.Equal(
            HttpStatusCode.OK,
            unchangedResponse.StatusCode);
        Assert.Equal(
            updatedEntityTag,
            unchangedResponse.Headers.ETag?.Tag);
        Assert.Equal(
            storedAfterUpdate.Version,
            storedAfterNoOp.Version);
        Assert.Equal(
            storedAfterUpdate.UpdatedAt,
            storedAfterNoOp.UpdatedAt);
        Assert.Equal(
            HttpStatusCode.OK,
            clearedResponse.StatusCode);
        Assert.NotEqual(
            updatedEntityTag,
            clearedEntityTag);
        Assert.Null(storedAfterClear.EventDate);
        Assert.Null(storedAfterClear.Message);
        Assert.Equal(
            _referenceTime.AddMinutes(3).UtcDateTime,
            storedAfterClear.UpdatedAt);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            staleResponse.StatusCode);
        var staleError = await staleResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(staleError);
        Assert.Equal(
            ErrorCodes.WishlistVersionConflict,
            staleError.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenExistingDateIsPast_AllowsUnchangedDateAndRejectsAnotherPastDate()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        var wishlist = await SeedWishlistAsync(
            factory,
            Guid.CreateVersion7(),
            owner.Id,
            "Liste passée",
            new DateOnly(
                2020,
                1,
                1));
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var retrievalResponse = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}",
            TestContext.Current.CancellationToken);
        var initialEntityTag = retrievalResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The wishlist ETag is missing.");

        // Act
        using var acceptedResponse = await UpdateWishlistAsync(
            client,
            wishlist.Id,
            initialEntityTag,
            "Liste passée renommée",
            "birthday",
            "2020-01-01",
            null);
        var acceptedEntityTag = acceptedResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The updated wishlist ETag is missing.");
        var acceptedWishlist = await GetWishlistAsync(
            factory,
            wishlist.Id);
        using var rejectedResponse = await UpdateWishlistAsync(
            client,
            wishlist.Id,
            acceptedEntityTag,
            "Liste passée renommée",
            "birthday",
            "2021-01-01",
            null);
        var unchangedWishlist = await GetWishlistAsync(
            factory,
            wishlist.Id);
        using var clearedResponse = await UpdateWishlistAsync(
            client,
            wishlist.Id,
            acceptedEntityTag,
            "Liste passée renommée",
            "birthday",
            null,
            null);
        var clearedWishlist = await GetWishlistAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            acceptedResponse.StatusCode);
        Assert.Equal(
            new DateOnly(
                2020,
                1,
                1),
            acceptedWishlist.EventDate);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            rejectedResponse.StatusCode);
        var error = await rejectedResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error.ErrorCode);
        var validationError = Assert.Single(error.ValidationErrors ?? []);
        Assert.Equal(
            "eventDate",
            validationError.PropertyName);
        Assert.Equal(
            acceptedWishlist.Version,
            unchangedWishlist.Version);
        Assert.Equal(
            acceptedWishlist.EventDate,
            unchangedWishlist.EventDate);
        Assert.Equal(
            HttpStatusCode.OK,
            clearedResponse.StatusCode);
        Assert.Null(clearedWishlist.EventDate);
    }

    [Fact]
    public async Task UpdateAsync_WhenNormalizedNameAlreadyExists_ReturnsConflictWithoutChangingWishlist()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var firstResponse = await CreateWishlistAsync(
            client,
            "Liste de Léa");
        using var secondResponse = await CreateWishlistAsync(
            client,
            "Liste de Jenn");
        var secondId = GetWishlistId(secondResponse);
        var secondEntityTag = secondResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The second wishlist ETag is missing.");

        // Act
        using var response = await UpdateWishlistAsync(
            client,
            secondId,
            secondEntityTag,
            "  LISTE DE LÉA  ",
            "birthday",
            "2099-09-24",
            "Merci d’être là");
        var stored = await GetWishlistAsync(
            factory,
            secondId);

        // Assert
        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNameAlreadyExists,
            error.ErrorCode);
        Assert.Equal(
            "Liste de Jenn",
            stored.Name);
    }

    [Fact]
    public async Task UpdateAsync_WhenWishlistBelongsToAnotherMember_ReturnsNotFoundWithoutChangingWishlist()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        var otherMember = await CreateMemberAsync(
            factory,
            "other@example.fr");
        using var ownerClient = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var otherClient = CreateAuthorizedClient(
            factory,
            otherMember.Id);
        using var creationResponse = await CreateWishlistAsync(
            ownerClient,
            "Liste privée");
        var wishlistId = GetWishlistId(creationResponse);
        var entityTag = creationResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The wishlist ETag is missing.");

        // Act
        using var response = await UpdateWishlistAsync(
            otherClient,
            wishlistId,
            entityTag,
            "Intrusion",
            "other",
            null,
            null);
        var stored = await GetWishlistAsync(
            factory,
            wishlistId);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        Assert.Equal(
            "Liste privée",
            stored.Name);
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrentRequestsUseSameVersion_ReturnsSuccessThenPreconditionFailed()
    {
        // Arrange
        var coordinator = new FirstSaveChangesCoordinator();
        await using var factory = await CreateMigratedFactoryAsync(
            new FixedTimeProvider(_referenceTime),
            coordinator);
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        var wishlist = await SeedWishlistAsync(
            factory,
            Guid.CreateVersion7(),
            owner.Id,
            "Liste initiale",
            null);
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var retrievalResponse = await client.GetAsync(
            $"/api/v1/wishlists/{wishlist.Id}",
            TestContext.Current.CancellationToken);
        var entityTag = retrievalResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The wishlist ETag is missing.");

        // Act
        var firstUpdateTask = UpdateWishlistAsync(
            client,
            wishlist.Id,
            entityTag,
            "Première modification",
            "other",
            null,
            null);
        await coordinator.WaitUntilFirstSaveStartsAsync(TestContext.Current.CancellationToken);
        HttpResponseMessage secondResponse;

        try
        {
            secondResponse = await UpdateWishlistAsync(
                client,
                wishlist.Id,
                entityTag,
                "Deuxième modification",
                "other",
                null,
                null);
        }
        finally
        {
            coordinator.ReleaseFirstSave();
        }

        using (secondResponse)
        using (var firstResponse = await firstUpdateTask)
        {
            var stored = await GetWishlistAsync(
                factory,
                wishlist.Id);
            var conflict = await firstResponse.Content.ReadFromJsonAsync<ErrorResponse>(
                TestContext.Current.CancellationToken);

            // Assert
            Assert.Equal(
                HttpStatusCode.OK,
                secondResponse.StatusCode);
            Assert.Equal(
                HttpStatusCode.PreconditionFailed,
                firstResponse.StatusCode);
            Assert.NotNull(conflict);
            Assert.Equal(
                ErrorCodes.WishlistVersionConflict,
                conflict.ErrorCode);
            Assert.Equal(
                "Deuxième modification",
                stored.Name);
        }
    }

    [Fact]
    public async Task DeleteMemberAsync_WhenMemberOwnsWishlist_DeletesWishlistInCascade()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishlistAsync(
            client,
            "Liste privée");
        Assert.Equal(
            1,
            await CountWishlistsAsync(factory));

        // Act
        await DeleteMemberAsync(
            factory,
            owner.Id);

        // Assert
        Assert.Equal(
            0,
            await CountWishlistsAsync(factory));
    }

    [Fact]
    public async Task CreateAsync_WhenAuthenticatedMemberDoesNotExist_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await CreateWishlistAsync(
            client,
            "Liste privée");

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
        Assert.Equal(
            0,
            await CountWishlistsAsync(factory));
    }

    [Fact]
    public async Task GetAsync_WhenAuthenticatedMemberWasDeleted_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        var owner = await CreateMemberAsync(
            factory,
            "owner@example.fr");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishlistAsync(
            client,
            "Liste privée");
        var location = creationResponse.Headers.Location
            ?? throw new InvalidOperationException("The wishlist location is missing.");
        await DeleteMemberAsync(
            factory,
            owner.Id);

        // Act
        using var response = await client.GetAsync(
            location,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync()
    {
        return await CreateMigratedFactoryAsync(new FixedTimeProvider(_referenceTime));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync(
        TimeProvider timeProvider,
        FirstSaveChangesCoordinator? coordinator = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider,
            configureServices: services => ConfigureCoordinatedUnitOfWork(
                services,
                coordinator));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);

        return factory;
    }

    private static void ConfigureCoordinatedUnitOfWork(
        IServiceCollection services,
        FirstSaveChangesCoordinator? coordinator)
    {

        if (coordinator is null)
            return;

        services.RemoveAll<IUnitOfWork>();
        services.AddSingleton(coordinator);
        services.AddScoped<IUnitOfWork, CoordinatedUnitOfWork>();
    }

    private static async Task<MonKadoUser> CreateMemberAsync(
        PostgreSqlApiFactory factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_referenceTime),
            Email = email,
            UserName = email,
            DisplayName = "Jenn",
            EmailConfirmed = true
        };
        var creationResult = await userManager.CreateAsync(member);
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

    private static async Task<HttpResponseMessage> CreateWishlistAsync(
        HttpClient client,
        string name)
    {
        return await CreateWishlistAsync(
            client,
            name,
            "2099-09-24");
    }

    private static async Task<HttpResponseMessage> CreateWishlistAsync(
        HttpClient client,
        string name,
        string? eventDate)
    {
        return await client.PostAsJsonAsync(
            "/api/v1/wishlists",
            new
            {
                name,
                occasion = "birthday",
                eventDate,
                message = "  Merci d’être là  "
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> UpdateWishlistAsync(
        HttpClient client,
        Guid wishlistId,
        string entityTag,
        string name,
        string occasion,
        string? eventDate,
        string? message)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}")
        {
            Content = JsonContent.Create(new
            {
                name,
                occasion,
                eventDate,
                message
            })
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            entityTag);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static Guid GetWishlistId(HttpResponseMessage response)
    {
        var location = response.Headers.Location
            ?? throw new InvalidOperationException("The wishlist location is missing.");

        return Guid.Parse(location.Segments[^1]);
    }

    private static async Task<Wishlist> SeedWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId,
        Guid ownerId,
        string name,
        DateOnly? eventDate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wishlist = new Wishlist(
            wishlistId,
            ownerId,
            name,
            name.ToUpperInvariant(),
            WishlistOccasion.Birthday,
            eventDate,
            null);
        context.Wishlists.Add(wishlist);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return wishlist;
    }

    private static async Task<Wishlist> GetWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishlists
            .AsNoTracking()
            .SingleAsync(
                wishlist => wishlist.Id == wishlistId,
                TestContext.Current.CancellationToken);
    }

    private static async Task<int> CountWishlistsAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishlists.CountAsync(TestContext.Current.CancellationToken);
    }

    private static async Task DeleteMemberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }
}
