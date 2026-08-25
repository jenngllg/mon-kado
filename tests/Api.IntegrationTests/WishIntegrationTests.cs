using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishIntegrationTests(PostgreSqlContainerFixture fixture)
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
    public async Task CreateAsync_WhenRequestIsValid_PersistsAndRetrievesNestedWishWithoutUpdatingParent()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste anniversaire");
        var parentVersion = wishlist.Version;
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);

        // Act
        using var creationResponse = await CreateWishAsync(
            client,
            wishlist.Id,
            "  Cafe\u0301  ");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();
        using var retrievalResponse = await client.GetAsync(
            creationResponse.Headers.Location,
            TestContext.Current.CancellationToken);
        var retrieved = await retrievalResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var storedWish = await GetWishAsync(
            factory,
            wishlist.Id,
            wishId);
        var storedParent = await GetWishlistAsync(
            factory,
            wishlist.Id);
        var nextPosition = await GetNextPositionAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            creationResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            retrievalResponse.StatusCode);
        Assert.Equal(
            wishId,
            retrieved.GetProperty("id").GetGuid());
        Assert.Equal(
            wishlist.Id,
            storedWish.WishlistId);
        Assert.Equal(
            7,
            storedWish.Id.Version);
        Assert.Equal(
            "Café",
            storedWish.Name);
        Assert.Equal(
            "Édition blanche",
            storedWish.Note);
        Assert.Equal(
            "https://example.com/gift",
            storedWish.Url);
        Assert.Equal(
            12.34m,
            storedWish.Price);
        Assert.Equal(
            1,
            storedWish.Position);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            storedWish.CreatedAt);
        Assert.Null(storedWish.UpdatedAt);
        Assert.NotEqual(
            0u,
            storedWish.Version);
        Assert.Equal(
            1,
            nextPosition);
        Assert.Equal(
            parentVersion,
            storedParent.Version);
        Assert.Null(storedParent.UpdatedAt);
        Assert.Equal(
            creationResponse.Headers.ETag?.Tag,
            retrievalResponse.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task CreateAsync_WhenTwoEqualNamesAreCreatedConcurrently_AllocatesDistinctSequentialPositions()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste parallèle");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);

        // Act
        var firstTask = CreateWishAsync(
            client,
            wishlist.Id,
            "Même cadeau");
        var secondTask = CreateWishAsync(
            client,
            wishlist.Id,
            "Même cadeau");
        var responses = await Task.WhenAll(
            firstTask,
            secondTask);
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        var storedWishes = await GetWishesAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.All(
            responses,
            response => Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode));
        Assert.Equal(
            2,
            storedWishes.Count);
        Assert.All(
            storedWishes,
            wish => Assert.Equal(
                "Même cadeau",
                wish.Name));
        Assert.Equal(
            [
                1L,
                2L
            ],
            storedWishes
                .Select(wish => wish.Position)
                .OrderBy(position => position));
    }

    [Fact]
    public async Task GetAsync_WhenWishBelongsToDifferentParent_ReturnsNotFound()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var firstWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Première liste");
        var secondWishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Deuxième liste");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        using var creationResponse = await CreateWishAsync(
            client,
            firstWishlist.Id,
            "Cadeau privé");
        var created = await creationResponse.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        var wishId = created.GetProperty("id").GetGuid();

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{secondWishlist.Id}/wishes/{wishId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishNotFound,
            error.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenCommitAcknowledgementIsLost_ReturnsSingleCommittedWish()
    {
        // Arrange
        var interceptor = new AmbiguousCommitInterceptor();
        await using var factory = await CreateFactoryAsync(services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor));
        });
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste ambiguë");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        interceptor.Arm();

        // Act
        using var response = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau ambigu");
        var storedWishes = await GetWishesAsync(
            factory,
            wishlist.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        var storedWish = Assert.Single(storedWishes);
        Assert.Equal(
            "Cadeau ambigu",
            storedWish.Name);
    }

    [Fact]
    public async Task CreateAsync_WhenAuthenticatedMemberWasDeleted_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste supprimée");
        using var client = CreateAuthorizedClient(
            factory,
            owner.Id);
        await DeleteMemberAsync(
            factory,
            owner.Id);

        // Act
        using var response = await CreateWishAsync(
            client,
            wishlist.Id,
            "Cadeau");

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
    public async Task AllocatePositionAsync_WhenConnectionIsAlreadyOpen_KeepsConnectionOpen()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste connexion ouverte");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IWishRepository>();
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

        // Act
        var position = await repository.AllocatePositionAsync(
            wishlist.Id,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            position);
        Assert.Equal(
            ConnectionState.Open,
            context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenUrlContainsCredentials_RejectsCredentials()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste URL");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.Wishes.Add(new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "URL interdite",
            null,
            "https://user:password@example.com/gift",
            null,
            1));

        // Act
        var action = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DbUpdateException>(action);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenUrlContainsAtInPath_PersistsWish()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var owner = await CreateMemberAsync(factory);
        var wishlist = await SeedWishlistAsync(
            factory,
            owner.Id,
            "Liste URL valide");
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.Wishes.Add(new Wish(
            Guid.CreateVersion7(),
            wishlist.Id,
            "URL autorisée",
            null,
            "https://example.com/path@value",
            null,
            1));

        // Act
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var url = await context.Wishes
            .AsNoTracking()
            .Select(wish => wish.Url)
            .SingleAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://example.com/path@value",
            url);
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(_referenceTime),
            configureServices: configureServices);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<MonKadoUser> CreateMemberAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_referenceTime),
            Email = "owner@example.fr",
            UserName = "owner@example.fr",
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
        var jwtOptions = factory.Services.GetRequiredService<IOptions<JwtOptions>>();
        var accessTokenService = new JwtAccessTokenService(
            jwtOptions,
            TimeProvider.System);
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static async Task<HttpResponseMessage> CreateWishAsync(
        HttpClient client,
        Guid wishlistId,
        string name)
    {
        return await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            new
            {
                name,
                note = "  Édition blanche  ",
                url = "  https://example.com/gift  ",
                price = 12.34m
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<Wishlist> SeedWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid ownerId,
        string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var wishlist = new Wishlist(
            Guid.CreateVersion7(),
            ownerId,
            name,
            name.ToUpperInvariant(),
            WishlistOccasion.Birthday,
            null,
            null);
        context.Wishlists.Add(wishlist);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return wishlist;
    }

    private static async Task<Wish> GetWishAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId,
        Guid wishId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishes
            .AsNoTracking()
            .SingleAsync(
                wish => wish.WishlistId == wishlistId && wish.Id == wishId,
                TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyCollection<Wish>> GetWishesAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Wishes
            .AsNoTracking()
            .Where(wish => wish.WishlistId == wishlistId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
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

    private static async Task<long> GetNextPositionAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT next_position FROM public.wish_position_sequences WHERE wishlist_id = @wishlist_id;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "wishlist_id";
        parameter.Value = wishlistId;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
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
