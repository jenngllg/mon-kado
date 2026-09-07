using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Creates complete report review scenarios and sends explicit administrative updates.</summary>
public static class WishlistReportReviewTestData
{
    /// <summary>Creates an administrator and another member's reported wishlist.</summary>
    /// <param name="factory">The PostgreSQL API host.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The accounts, wishlist, and pending report.</returns>
    public static async Task<(Guid AdminId, Guid OwnerId, Wishlist Wishlist, WishlistReport Report)> PrepareAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        var adminId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var ownerId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        var wishlist = await ReportedWishlistTestData.CreateWishlistAsync(
            factory,
            ownerId,
            cancellationToken);
        var report = await ReportedWishlistTestData.AddReportAsync(
            factory,
            wishlist.Id,
            WishlistReportReason.Other,
            cancellationToken);

        return (adminId, ownerId, wishlist, report);
    }

    /// <summary>Builds the parent-scoped report resource path.</summary>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    /// <returns>The relative report URI.</returns>
    public static string Route(
        Guid wishlistId,
        Guid reportId)
    {

        return $"/api/v1/admin/reported-wishlists/{wishlistId}/reports/{reportId}";
    }

    /// <summary>Sends an explicit report status and note with a concurrency precondition.</summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="route">The report route.</param>
    /// <param name="tag">The expected report ETag.</param>
    /// <param name="status">The requested JSON status.</param>
    /// <param name="note">The private note.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The HTTP response owned by the caller.</returns>
    public static async Task<HttpResponseMessage> UpdateAsync(
        HttpClient client,
        string route,
        string tag,
        string status,
        string? note,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            route);
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(tag));
        request.Content = JsonContent.Create(new
        {
            status,
            reviewNote = note
        });

        return await client.SendAsync(
            request,
            cancellationToken);
    }
}
