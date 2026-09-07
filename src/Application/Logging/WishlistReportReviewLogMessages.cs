using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Logs administrative report review operations using technical identifiers only.</summary>
public static partial class WishlistReportReviewLogMessages
{
    /// <summary>Logs a successful report read without its private content.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistReportRetrieved, Level = LogLevel.Information, Message = "WishlistReportRetrieved: wishlist {WishlistId}, report {ReportId}.")]
    public static partial void WishlistReportRetrieved(
        ILogger logger,
        Guid wishlistId,
        Guid reportId);
    /// <summary>Logs a successful review history read without its private notes.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistReportReviewEventsRetrieved, Level = LogLevel.Information, Message = "WishlistReportReviewEventsRetrieved: wishlist {WishlistId}, report {ReportId}.")]
    public static partial void WishlistReportReviewEventsRetrieved(
        ILogger logger,
        Guid wishlistId,
        Guid reportId);
    /// <summary>Logs a successful review request without the decision content.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistReportReviewed, Level = LogLevel.Information, Message = "WishlistReportReviewed: wishlist {WishlistId}, report {ReportId}.")]
    public static partial void WishlistReportReviewed(
        ILogger logger,
        Guid wishlistId,
        Guid reportId);
    /// <summary>Traces the start of report review processing.</summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistReportReviewStarted, Level = LogLevel.Debug, Message = "WishlistReportReviewStarted: wishlist {WishlistId}, report {ReportId}.")]
    public static partial void WishlistReportReviewStarted(
        ILogger logger,
        Guid wishlistId,
        Guid reportId);
}
