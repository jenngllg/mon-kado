using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains read-only administrative audit data.</summary>
[ExcludeFromCodeCoverage]
public class AdministrativeAuditFilter
{
    /// <summary>Gets the Action value.</summary>
    public AdministrativeAuditAction? Action
    {
        get; init;
    }
    /// <summary>Gets the AdministratorId value.</summary>
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>Gets the MemberId value.</summary>
    public Guid? MemberId
    {
        get; init;
    }
    /// <summary>Gets the WishlistId value.</summary>
    public Guid? WishlistId
    {
        get; init;
    }
    /// <summary>Gets the ExportId value.</summary>
    public Guid? ExportId
    {
        get; init;
    }
    /// <summary>Gets the RequestReference value.</summary>
    public string? RequestReference
    {
        get; init;
    }
    /// <summary>Gets the From value.</summary>
    public DateTime? From
    {
        get; init;
    }
    /// <summary>Gets the To value.</summary>
    public DateTime? To
    {
        get; init;
    }
    /// <summary>Gets the Page value.</summary>
    public int Page
    {
        get; init;
    }
    /// <summary>Gets the PageSize value.</summary>
    public int PageSize
    {
        get; init;
    }
}
