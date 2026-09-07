using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Sets up an administrator and applies real HTTP moderation in cross-feature tests.</summary>
public static class WishlistModerationHttpTestHelper
{
    /// <summary>Grants the database role to an existing isolated test account.</summary>
    /// <param name="factory">The test application.</param>
    /// <param name="memberId">The test administrator identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the assignment is persisted.</returns>
    public static async Task GrantAdministratorAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = memberId,
            RoleId = RoleIds.Admin
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Changes moderation using its current public ETag and verifies HTTP success.</summary>
    /// <param name="administrator">The authenticated administrator client.</param>
    /// <param name="wishlistId">The target wishlist identifier.</param>
    /// <param name="isSuspended">The desired suspension state.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the decision is committed.</returns>
    public static async Task SetStateAsync(
        HttpClient administrator,
        Guid wishlistId,
        bool isSuspended,
        CancellationToken cancellationToken)
    {
        var route = $"/api/v1/admin/wishlists/{wishlistId}/moderation";
        using var current = await administrator.GetAsync(
            route,
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            route)
        {
            Content = JsonContent.Create(new { isSuspended, reason = isSuspended ? "Private moderation reason" : null })
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            Assert.IsType<string>(current.Headers.ETag?.Tag));
        using var response = await administrator.SendAsync(
            request,
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }
}
