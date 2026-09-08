using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public static class AdministrativeAuditTestData
{
    public static async Task<AdministrativeAuditScenario> CreateCompleteJournalAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        var readerId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var actorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            memberId,
            cancellationToken);
        var now = new DateTime(
            2026,
            9,
            1,
            12,
            0,
            0,
            DateTimeKind.Utc);
        var exportId = Guid.CreateVersion7();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        foreach (var action in Enum.GetValues<WishlistModerationAction>())
        {
            context.WishlistModerationEvents.Add(new WishlistModerationEvent(
                Guid.CreateVersion7(),
                wishlist.Id,
                actorId,
                (int)action + 1,
                action,
                action == WishlistModerationAction.Reactivated ? null : "Private moderation reason",
                now));
        }
        foreach (var action in Enum.GetValues<AdministrativeDataExportAction>())
        {
            context.AdministrativeDataExportEvents.Add(new AdministrativeDataExportEvent(
                actorId,
                memberId,
                exportId,
                action,
                "SUPPORT-806",
                now));
        }
        context.AdministrativeAccountErasureEvents.Add(new AdministrativeAccountErasureEvent(
            actorId,
            memberId,
            "SUPPORT-806",
            now,
            AccountErasureNotificationStatus.NotApplicable));
        await context.SaveChangesAsync(cancellationToken);

        return new AdministrativeAuditScenario
        {
            ReaderId = readerId,
            ActorId = actorId,
            MemberId = memberId,
            WishlistId = wishlist.Id,
            ExportId = exportId,
            CreatedAt = now
        };
    }
}
