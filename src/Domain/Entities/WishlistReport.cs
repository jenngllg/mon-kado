using JennGllg.Fr.MonKado.Back.Domain.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Domain.Entities;

/// <summary>
/// Represents an anonymous report submitted for a shared wishlist.
/// </summary>
public class WishlistReport : IAuditableEntity
{
    private WishlistReport()
    {
    }

    /// <summary>
    /// Initializes a wishlist report.
    /// </summary>
    /// <param name="id">The report identifier.</param>
    /// <param name="wishlistId">The reported wishlist identifier.</param>
    /// <param name="reason">The report reason.</param>
    /// <param name="details">The optional normalized details.</param>
    public WishlistReport(
        Guid id,
        Guid wishlistId,
        WishlistReportReason reason,
        string? details)
    {
        Id = id;
        WishlistId = wishlistId;
        Reason = reason;
        Details = details;
    }

    /// <summary>Gets the report identifier.</summary>
    public Guid Id
    {
        get; private set;
    }

    /// <summary>Gets the reported wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; private set;
    }

    /// <summary>Gets the report reason.</summary>
    public WishlistReportReason Reason
    {
        get; private set;
    }

    /// <summary>Gets the optional visitor-provided details.</summary>
    public string? Details
    {
        get; private set;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework sets this private setter through change tracking.")]
    public DateTime CreatedAt
    {
        get; private set;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "CodeQuality",
        "S1144:Unused private types or members should be removed",
        Justification = "Entity Framework sets this private setter through change tracking.")]
    public DateTime? UpdatedAt
    {
        get; private set;
    }
}
