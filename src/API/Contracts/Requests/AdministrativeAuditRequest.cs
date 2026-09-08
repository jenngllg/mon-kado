using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Mvc;

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains optional global audit filters without silently converting blank strings to absence.</summary>
[ExcludeFromCodeCoverage]
public class AdministrativeAuditRequest
{
    /// <summary>Gets the optional Action filter.</summary>
    [FromQuery(Name = "action")]
    public AdministrativeAuditAction? Action
    {
        get; init;
    }
    /// <summary>Gets the optional AdministratorId filter.</summary>
    [FromQuery(Name = "administratorId")]
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>Gets the optional MemberId filter.</summary>
    [FromQuery(Name = "memberId")]
    public Guid? MemberId
    {
        get; init;
    }
    /// <summary>Gets the optional WishlistId filter.</summary>
    [FromQuery(Name = "wishlistId")]
    public Guid? WishlistId
    {
        get; init;
    }
    /// <summary>Gets the optional ExportId filter.</summary>
    [FromQuery(Name = "exportId")]
    public Guid? ExportId
    {
        get; init;
    }
    /// <summary>Gets the optional RequestReference filter.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [FromQuery(Name = "requestReference")]
    public string? RequestReference
    {
        get; init;
    }
    /// <summary>Gets the optional From filter.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [FromQuery(Name = "from")]
    public string? From
    {
        get; init;
    }
    /// <summary>Gets the optional To filter.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [FromQuery(Name = "to")]
    public string? To
    {
        get; init;
    }
    /// <summary>Gets the optional Page filter.</summary>
    [FromQuery(Name = "page")]
    public int? Page
    {
        get; init;
    }
    /// <summary>Gets the optional PageSize filter.</summary>
    [FromQuery(Name = "pageSize")]
    public int? PageSize
    {
        get; init;
    }
}
