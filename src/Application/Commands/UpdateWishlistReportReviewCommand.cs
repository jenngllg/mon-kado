using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests a private administrative report review operation.</summary>
/// <param name="administratorId">The administratorId.</param>
/// <param name="wishlistId">The wishlistId.</param>
/// <param name="reportId">The reportId.</param>
/// <param name="status">The status.</param>
/// <param name="reviewNote">The reviewNote.</param>
/// <param name="expectedVersion">The expectedVersion.</param>
public class UpdateWishlistReportReviewCommand(
    Guid administratorId,
    Guid wishlistId,
    Guid reportId,
    WishlistReportStatus? status,
    string? reviewNote,
    uint expectedVersion) : IRequest<VersionedWishlistReportDetails>
{
    /// <summary>Gets the administratorId.</summary>
    public Guid AdministratorId { get; } = administratorId;
    /// <summary>Gets the wishlistId.</summary>
    public Guid WishlistId { get; } = wishlistId;
    /// <summary>Gets the reportId.</summary>
    public Guid ReportId { get; } = reportId;
    /// <summary>Gets the status.</summary>
    public WishlistReportStatus? Status { get; } = status;
    /// <summary>Gets the reviewNote.</summary>
    public string? ReviewNote { get; } = reviewNote;
    /// <summary>Gets the expectedVersion.</summary>
    public uint ExpectedVersion { get; } = expectedVersion;
}

/// <summary>Coordinates a validated report review operation without logging private notes.</summary>
/// <param name="service">The report review service.</param>
/// <param name="logger">The structured logger.</param>
public class UpdateWishlistReportReviewCommandHandler(
    IWishlistReportReviewService service,
    ILogger<UpdateWishlistReportReviewCommandHandler> logger) : IRequestHandler<UpdateWishlistReportReviewCommand, VersionedWishlistReportDetails>
{
    /// <summary>Executes the validated report review request.</summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The safe administrative result.</returns>
    public async Task<VersionedWishlistReportDetails> Handle(
        UpdateWishlistReportReviewCommand request,
        CancellationToken cancellationToken)
    {
        WishlistReportReviewLogMessages.WishlistReportReviewStarted(
            logger,
            request.WishlistId,
            request.ReportId);
        var result = await service.UpdateAsync(
            request.AdministratorId,
            request.WishlistId,
            request.ReportId,
            request.Status.GetValueOrDefault(),
            request.ReviewNote,
            request.ExpectedVersion,
            cancellationToken);
        WishlistReportReviewLogMessages.WishlistReportReviewed(
            logger,
            request.WishlistId,
            request.ReportId);

        return result;
    }
}
