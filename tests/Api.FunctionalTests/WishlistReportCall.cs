using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Describes one recorded anonymous wishlist report call.
/// </summary>
public class WishlistReportCall
{
    /// <summary>Initializes a recorded report call.</summary>
    public WishlistReportCall(
        Guid reportId,
        Guid shareLinkId,
        string shareSecret,
        WishlistReportReason reason,
        string? details)
    {
        ReportId = reportId;
        ShareLinkId = shareLinkId;
        ShareSecret = shareSecret;
        Reason = reason;
        Details = details;
    }

    /// <summary>Gets the generated report identifier.</summary>
    public Guid ReportId
    {
        get;
    }

    /// <summary>Gets the share-link identifier.</summary>
    public Guid ShareLinkId
    {
        get;
    }

    /// <summary>Gets the share-link secret.</summary>
    public string ShareSecret
    {
        get;
    }

    /// <summary>Gets the report reason.</summary>
    public WishlistReportReason Reason
    {
        get;
    }

    /// <summary>Gets the normalized details.</summary>
    public string? Details
    {
        get;
    }
}
