using JennGllg.Fr.MonKado.Back.Application.Models;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Contains read-only administrative audit data.</summary>
[ExcludeFromCodeCoverage]
public class AdministrativeAuditEventResponse
{
    /// <summary>The original event identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>The UTC event date.</summary>
    public DateTime CreatedAt
    {
        get; init;
    }
    /// <summary>The normalized administrative action.</summary>
    public AdministrativeAuditAction Action
    {
        get; init;
    }
    /// <summary>The actor identifier, or null after deletion.</summary>
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>The current actor display name, not a historical snapshot.</summary>
    public string? AdministratorDisplayName
    {
        get; init;
    }
    /// <summary>The moderated wishlist identifier.</summary>
    public Guid? WishlistId
    {
        get; init;
    }
    /// <summary>The recorded GDPR target identifier, if still retained.</summary>
    public Guid? MemberId
    {
        get; init;
    }
    /// <summary>The recorded archive identifier.</summary>
    public Guid? ExportId
    {
        get; init;
    }
    /// <summary>The recorded private moderation reason.</summary>
    public string? Reason
    {
        get; init;
    }
    /// <summary>The recorded support reference.</summary>
    public string? RequestReference
    {
        get; init;
    }
}
