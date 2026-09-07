using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Net.Http.Headers;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public static class ReportedWishlistTestData
{
    public static async Task<Guid> CreateAdministratorAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {

        return await CreateMemberAsync(
            factory,
            RoleNames.Admin,
            cancellationToken);
    }

    public static async Task<Guid> CreateOwnerAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {

        return await CreateMemberAsync(
            factory,
            RoleNames.Member,
            cancellationToken);
    }

    private static async Task<Guid> CreateMemberAsync(
        PostgreSqlApiFactory factory,
        string role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var fixture = TestFixture.Create();
        var id = fixture.Create<Guid>();
        var email = $"{id:N}@example.test";
        var member = fixture
            .Build<MonKadoUser>()
            .OmitAutoProperties()
            .With(
            user => user.Id,
            id)
            .With(
            user => user.Email,
            email)
            .With(
            user => user.UserName,
            email)
            .With(
            user => user.DisplayName,
            "Private owner name")
            .With(
            user => user.EmailConfirmed,
            true)
            .Create();
        Assert.True((await manager.CreateAsync(member)).Succeeded);
        Assert.True((await manager.AddToRoleAsync(
                member,
                role)).Succeeded);

        return id;
    }

    public static async Task<Wishlist> CreateWishlistAsync(
        PostgreSqlApiFactory factory,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var fixture = TestFixture.Create();
        var name = fixture.Create<string>();
        var wishlist = new Wishlist(
            fixture.Create<Guid>(),
            ownerId,
            name,
            name.ToUpperInvariant(),
            WishlistOccasion.Other,
            null,
            "Private owner message");
        context.Wishlists.Add(wishlist);
        await context.SaveChangesAsync(cancellationToken);

        return wishlist;
    }

    public static async Task<WishlistReport> AddReportAsync(
        PostgreSqlApiFactory factory,
        Guid wishlistId,
        WishlistReportReason reason,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var report = new WishlistReport(
            Guid.CreateVersion7(),
            wishlistId,
            reason,
            "Private anonymous report details");
        context.WishlistReports.Add(report);
        await context.SaveChangesAsync(cancellationToken);

        return report;
    }

    public static HttpClient CreateClient(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var service = new JwtAccessTokenService(
            factory.Services.GetRequiredService<IOptions<JwtOptions>>(),
            TimeProvider.System);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            service
                .Create(memberId)
                .Value);

        return client;
    }
}
