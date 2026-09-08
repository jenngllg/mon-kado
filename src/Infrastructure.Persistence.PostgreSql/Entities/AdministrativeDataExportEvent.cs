using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Preserves administrative export accountability independently of archive cleanup.</summary>
/// <param name="administratorId">The acting administrator identifier.</param>
/// <param name="memberId">The target account identifier.</param>
/// <param name="exportId">The immutable export identifier, retained after archive cleanup.</param>
/// <param name="action">The audited action.</param>
/// <param name="requestReference">The non-sensitive external request reference.</param>
/// <param name="createdAt">The UTC event date.</param>
public class AdministrativeDataExportEvent(
    Guid? administratorId,
    Guid? memberId,
    Guid exportId,
    AdministrativeDataExportAction action,
    string requestReference,
    DateTime createdAt)
{
    /// <summary>Gets the application-generated event identifier.</summary>
    public Guid Id { get; private set; } = Guid.CreateVersion7();
    /// <summary>Gets the actor, or null after account deletion.</summary>
    public Guid? AdministratorId { get; private set; } = administratorId;
    /// <summary>Gets the target, or null after account deletion.</summary>
    public Guid? MemberId { get; private set; } = memberId;
    /// <summary>Gets the export correlation identifier without retaining its database row.</summary>
    public Guid ExportId { get; private set; } = exportId;
    /// <summary>Gets the bounded action.</summary>
    public AdministrativeDataExportAction Action { get; private set; } = action;
    /// <summary>Gets the request reference, which must never be included in application logs.</summary>
    public string RequestReference { get; private set; } = requestReference;
    /// <summary>Gets the UTC event date.</summary>
    public DateTime CreatedAt { get; private set; } = createdAt;
}
