using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Logs administrative reads using technical identifiers only.</summary>
public static partial class ReportedWishlistLogMessages
{
    /// <summary>Logs a successful administrative read.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(EventId = LogEventIds.ReportedWishlistsRetrieved, Level = LogLevel.Information, Message = "Retrieved reported wishlists.")]
    public static partial void ReportedWishlistsRetrieved(ILogger logger);
    /// <summary>Logs a successful administrative read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ReportedWishlistRetrieved, Level = LogLevel.Information, Message = "Retrieved current reported wishlist {WishlistId}.")]
    public static partial void ReportedWishlistRetrieved(
        ILogger logger,
        Guid wishlistId);
    /// <summary>Logs a successful administrative read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.WishlistReportsRetrieved, Level = LogLevel.Information, Message = "Retrieved anonymous reports for wishlist {WishlistId}.")]
    public static partial void WishlistReportsRetrieved(
        ILogger logger,
        Guid wishlistId);
    /// <summary>Logs a successful administrative read.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The technical identifier.</param>
    /// <param name="wishId">The technical identifier.</param>
    [LoggerMessage(EventId = LogEventIds.ReportedWishImageRetrieved, Level = LogLevel.Information, Message = "Retrieved administrative image for wishlist {WishlistId}, wish {WishId}.")]
    public static partial void ReportedWishImageRetrieved(
        ILogger logger,
        Guid wishlistId,
        Guid wishId);
}
