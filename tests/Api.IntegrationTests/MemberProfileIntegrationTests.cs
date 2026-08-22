using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
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

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class MemberProfileIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset _referenceTime = DateTimeOffset.UtcNow;

    [Fact]
    public async Task UpdateProfileAsync_WhenUsingEntityTags_UpdatesAndRejectsStaleVersion()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(factory);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        using var currentResponse = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            currentResponse.StatusCode);
        var initialEntityTag = currentResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The current session ETag is missing.");
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        // Act
        using var updateResponse = await UpdateAsync(
            client,
            " Jennifer ",
            initialEntityTag);
        var updatedEntityTag = updateResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The updated profile ETag is missing.");
        using var staleResponse = await UpdateAsync(
            client,
            "Jen",
            initialEntityTag);
        var updatedMember = await GetMemberAsync(
            factory,
            member.Id);
        var updatedAt = updatedMember.UpdatedAt;
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        using var unchangedResponse = await UpdateAsync(
            client,
            " Jennifer ",
            updatedEntityTag);
        var unchangedMember = await GetMemberAsync(
            factory,
            member.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            updateResponse.StatusCode);
        Assert.NotEqual(
            initialEntityTag,
            updatedEntityTag);
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            staleResponse.StatusCode);
        var staleError = await staleResponse.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(staleError);
        Assert.Equal(
            ErrorCodes.MemberProfileVersionConflict,
            staleError.ErrorCode);
        Assert.Equal(
            HttpStatusCode.OK,
            unchangedResponse.StatusCode);
        Assert.Equal(
            updatedEntityTag,
            unchangedResponse.Headers.ETag?.Tag);
        Assert.Equal(
            "Jennifer",
            unchangedMember.DisplayName);
        Assert.Equal(
            updatedAt,
            unchangedMember.UpdatedAt);
        Assert.Equal(
            updatedMember.Version,
            unchangedMember.Version);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenConcurrentRequestsUseSameVersion_ReturnsSuccessThenPreconditionFailed()
    {
        // Arrange
        var coordinator = new FirstSaveChangesCoordinator();
        await using var factory = await CreateMigratedFactoryAsync(
            new MutableTimeProvider(_referenceTime),
            coordinator);
        var member = await CreateMemberAsync(factory);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        using var currentResponse = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        var initialEntityTag = currentResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The current session ETag is missing.");

        // Act
        var firstUpdateTask = UpdateAsync(
            client,
            "First update",
            initialEntityTag);
        await coordinator.WaitUntilFirstSaveStartsAsync(TestContext.Current.CancellationToken);
        HttpResponseMessage secondResponse;

        try
        {
            secondResponse = await UpdateAsync(
                client,
                "Second update",
                initialEntityTag);
        }
        finally
        {
            coordinator.ReleaseFirstSave();
        }

        using (secondResponse)
        using (var firstResponse = await firstUpdateTask)
        {
            var storedMember = await GetMemberAsync(
                factory,
                member.Id);
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
                ErrorCodes.MemberProfileVersionConflict,
                conflict.ErrorCode);
            Assert.Equal(
                "Second update",
                storedMember.DisplayName);
        }
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenMemberRowChanges_ReturnsPreconditionFailedForPreviousEntityTag()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            new MutableTimeProvider(_referenceTime));
        var member = await CreateMemberAsync(factory);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        using var currentResponse = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        var initialEntityTag = currentResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The current session ETag is missing.");
        await UpdateMemberPhoneNumberAsync(
            factory,
            member.Id);

        // Act
        using var response = await UpdateAsync(
            client,
            "Jennifer",
            initialEntityTag);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        var storedMember = await GetMemberAsync(
            factory,
            member.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.MemberProfileVersionConflict,
            error.ErrorCode);
        Assert.Equal(
            "Jenn",
            storedMember.DisplayName);
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenMemberIsDeletedDuringUpdate_ReturnsUnauthorizedAndDeletesRefreshCookie()
    {
        // Arrange
        var coordinator = new FirstSaveChangesCoordinator();
        await using var factory = await CreateMigratedFactoryAsync(
            new MutableTimeProvider(_referenceTime),
            coordinator);
        var member = await CreateMemberAsync(factory);
        using var client = CreateAuthorizedClient(
            factory,
            member.Id);
        using var currentResponse = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        var initialEntityTag = currentResponse.Headers.ETag?.Tag
            ?? throw new InvalidOperationException("The current session ETag is missing.");

        // Act
        var updateTask = UpdateAsync(
            client,
            "Concurrent update",
            initialEntityTag);
        await coordinator.WaitUntilFirstSaveStartsAsync(TestContext.Current.CancellationToken);

        try
        {
            await DeleteMemberAsync(
                factory,
                member.Id);
        }
        finally
        {
            coordinator.ReleaseFirstSave();
        }

        using var response = await updateTask;
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        var memberExists = await MemberExistsAsync(
            factory,
            member.Id);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            error.ErrorCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        Assert.False(memberExists);
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

    private static async Task<MonKadoUser> CreateMemberAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_referenceTime),
            Email = "jenn@example.fr",
            UserName = "jenn@example.fr",
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

    private static async Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        string displayName,
        string entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            "/api/v1/members/current/profile")
        {
            Content = JsonContent.Create(new { displayName })
        };
        request.Headers.IfMatch.ParseAdd(entityTag);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<MonKadoUser> GetMemberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Users
            .AsNoTracking()
            .SingleAsync(
                member => member.Id == memberId,
                TestContext.Current.CancellationToken);
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

    private static async Task UpdateMemberPhoneNumberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.PhoneNumber,
                    "+33123456789"),
                TestContext.Current.CancellationToken);
    }

    private static async Task<bool> MemberExistsAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Users
            .AsNoTracking()
            .AnyAsync(
                member => member.Id == memberId,
                TestContext.Current.CancellationToken);
    }
}
