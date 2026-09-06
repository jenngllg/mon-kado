using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class UserSearchIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly string[] _publicProperties = [
        "id",
        "displayName"
    ];
    [Theory]
    [InlineData("elodie", "Élodie")]
    [InlineData("ÉLODIE", "SuperElodie")]
    [InlineData("e\u0301lodie", "Élodie")]
    [InlineData("elodie", "E\u0301lodie")]
    [InlineData("%_", "Literal%_Name")]
    [InlineData("  jen  ", "SuperJenn")]
    public async Task SearchAsync_WhenPublicQueryMatches_ReturnsOnlyConfirmedPublicIdentities(
        string term,
        string displayName)
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await CreateMemberAsync(
            manager,
            displayName,
            true);
        await CreateMemberAsync(
            manager,
            displayName,
            false);
        await CreateMemberAsync(
            manager,
            "Unrelated member",
            true);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(
            "/api/v1/members?displayName=" + Uri.EscapeDataString(term),
            TestContext.Current.CancellationToken);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            1,
            json
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            1,
            json
                .GetProperty("currentPage")
                .GetInt32());
        Assert.Equal(
            20,
            json
                .GetProperty("pageSize")
                .GetInt32());
        var item = Assert.Single(json
                .GetProperty("items")
                .EnumerateArray());
        Assert.Equal(
            _publicProperties,
            item
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            member.Id,
            item
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            displayName,
            item
                .GetProperty("displayName")
                .GetString());
    }

    [Fact]
    public async Task SearchAsync_WhenNamesChangeOrAccountsDisappear_ReflectsCurrentDatabaseState()
    {
        // Arrange
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var first = await CreateMemberAsync(
            manager,
            "Jen",
            true);
        var second = await CreateMemberAsync(
            manager,
            "jen",
            true);
        using var client = factory.CreateClient();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var orderedIds = new List<Guid>
        {
            first.Id,
            second.Id
        }
            .Order()
            .ToArray();

        // Act
        var page = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/members?displayName=jen&pageSize=1",
            TestContext.Current.CancellationToken);
        var next = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/members?displayName=jen&pageSize=1&page=2",
            TestContext.Current.CancellationToken);
        var beyond = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/members?displayName=jen&page=2147483647&pageSize=100",
            TestContext.Current.CancellationToken);
        first.DisplayName = "Other name";
        await manager.UpdateAsync(first);
        await manager.DeleteAsync(second);
        var empty = await client.GetFromJsonAsync<JsonElement>(
            "/api/v1/members?displayName=jen&page=2147483647&pageSize=100",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            page
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            orderedIds[0],
            Assert
                .Single(page
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Equal(
            orderedIds[1],
            Assert
                .Single(next
                    .GetProperty("items")
                    .EnumerateArray())
                .GetProperty("id")
                .GetGuid());
        Assert.Empty(empty
                .GetProperty("items")
                .EnumerateArray());
        Assert.Empty(beyond
            .GetProperty("items")
            .EnumerateArray());
        Assert.Equal(
            2,
            beyond
                .GetProperty("totalCount")
                .GetInt32());
        Assert.Equal(
            0,
            empty
                .GetProperty("totalCount")
                .GetInt32());
        Assert.False(empty
                .GetProperty("hasPreviousPage")
                .GetBoolean());
        Assert.False(await context.Users.AnyAsync(
                user => user.Id == second.Id,
                TestContext.Current.CancellationToken));
    }

    private static async Task<MonKadoUser> CreateMemberAsync(
        UserManager<MonKadoUser> manager,
        string displayName,
        bool confirmed)
    {
        var email = $"{Guid.CreateVersion7():N}@example.test";
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            UserName = email,
            DisplayName = displayName,
            EmailConfirmed = confirmed
        };
        var result = await manager.CreateAsync(member);
        Assert.True(result.Succeeded);

        return member;
    }
}
