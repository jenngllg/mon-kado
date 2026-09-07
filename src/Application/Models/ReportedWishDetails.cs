using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains read-only administrative report data.</summary>
[ExcludeFromCodeCoverage]
public class ReportedWishDetails
{
    /// <summary>The wish identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>The wish name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>The optional note.</summary>
    public string? Note
    {
        get; init;
    }
    /// <summary>The optional merchant URL.</summary>
    public string? Url
    {
        get; init;
    }
    /// <summary>The optional price.</summary>
    public decimal? Price
    {
        get; init;
    }
    /// <summary>The desired quantity.</summary>
    public int Quantity
    {
        get; init;
    }
    /// <summary>The current ordering position.</summary>
    public long Position
    {
        get; init;
    }
    /// <summary>The UTC creation date.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>The UTC modification date.</summary>
    public DateTime? UpdatedAt
    {
        get; init;
    }
    /// <summary>The current internal image identifier.</summary>
    public Guid? ImageId
    {
        get; init;
    }
}
