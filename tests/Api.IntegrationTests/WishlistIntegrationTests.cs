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
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(_referenceTime));
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
        return await client.PostAsJsonAsync(
            "/api/v1/wishlists",
            new
            {
                name,
                occasion = "birthday",
                eventDate = "2099-09-24",
                message = "  Merci d’être là  "
            },
            TestContext.Current.CancellationToken);
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
