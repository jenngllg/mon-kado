using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents an anonymous report submitted through a wishlist share link.
/// </summary>
public class ReportSharedWishlistCommand : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Initializes a shared-wishlist report command.
    /// </summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented share-link secret.</param>
    /// <param name="reason">The report reason.</param>
    /// <param name="details">The optional report details.</param>
    public ReportSharedWishlistCommand(
        Guid shareLinkId,
        string? secret,
        WishlistReportReason? reason,
        string? details)
    {
        ShareLinkId = shareLinkId;
        Secret = secret;
        Reason = reason;
        Details = details;
    }

    /// <summary>Gets the share-link identifier.</summary>
    public Guid ShareLinkId
    {
        get;
    }

    /// <summary>Gets the presented share-link secret.</summary>
    public string? Secret
    {
        get;
    }

    /// <summary>Gets the report reason.</summary>
    public WishlistReportReason? Reason
    {
        get;
    }

    /// <summary>Gets the optional report details.</summary>
    public string? Details
    {
        get;
    }

    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {

        if (ShareLinkId == Guid.Empty || string.IsNullOrWhiteSpace(Secret))
            return new SharedWishlistNotFoundException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles anonymous shared-wishlist reports.
/// </summary>
/// <param name="reportService">The wishlist report service.</param>
/// <param name="logger">The logger.</param>
public class ReportSharedWishlistCommandHandler(
    IWishlistReportService reportService,
    ILogger<ReportSharedWishlistCommandHandler> logger)
    : IRequestHandler<ReportSharedWishlistCommand>
{
    /// <summary>
    /// Verifies the share link and persists an anonymous report.
    /// </summary>
    /// <param name="request">The report command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task Handle(
        ReportSharedWishlistCommand request,
        CancellationToken cancellationToken)
    {
        var reportId = Guid.CreateVersion7();
        ApplicationLogMessages.WishlistReportCreationStarted(
            logger,
            request.ShareLinkId,
            reportId);
        var wishlistId = await reportService.CreateAsync(
            reportId,
            request.ShareLinkId,
            request.Secret ?? string.Empty,
            request.Reason ?? default,
            WishlistReportTextNormalizer.NormalizeDetails(request.Details),
            cancellationToken);
        ApplicationLogMessages.WishlistReportCreated(
            logger,
            wishlistId,
            reportId);
    }
}
