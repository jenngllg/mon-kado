using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests a global audit page through centralized validation.</summary>
public class GetAdministrativeAuditEventsQuery : IRequest<AdministrativeAuditPage>
{
    /// <summary>Gets the authenticated caller identifier, never supplied by the client.</summary>
    public Guid CallerId
    {
        get; init;
    }
    /// <summary>Gets the optional Action filter.</summary>
    public AdministrativeAuditAction? Action
    {
        get; init;
    }
    /// <summary>Gets the optional AdministratorId filter.</summary>
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>Gets the optional MemberId filter.</summary>
    public Guid? MemberId
    {
        get; init;
    }
    /// <summary>Gets the optional WishlistId filter.</summary>
    public Guid? WishlistId
    {
        get; init;
    }
    /// <summary>Gets the optional ExportId filter.</summary>
    public Guid? ExportId
    {
        get; init;
    }
    /// <summary>Gets the optional RequestReference filter.</summary>
    public string? RequestReference
    {
        get; init;
    }
    /// <summary>Gets the optional From filter.</summary>
    public string? From
    {
        get; init;
    }
    /// <summary>Gets the optional To filter.</summary>
    public string? To
    {
        get; init;
    }
    /// <summary>Gets the optional Page filter.</summary>
    public int? Page
    {
        get; init;
    }
    /// <summary>Gets the optional PageSize filter.</summary>
    public int? PageSize
    {
        get; init;
    }
}

/// <summary>Coordinates normalized audit reads without logging filter contents.</summary>
/// <param name="service">The snapshot reader.</param>
/// <param name="logger">The correlated technical logger.</param>
public class GetAdministrativeAuditEventsQueryHandler(
    IAdministrativeAuditService service,
    ILogger<GetAdministrativeAuditEventsQueryHandler> logger) : IRequestHandler<GetAdministrativeAuditEventsQuery, AdministrativeAuditPage>
{
    /// <summary>Reads the validated page and logs only its authenticated caller.</summary>
    /// <param name="request">The centrally validated request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The retained event page.</returns>
    public async Task<AdministrativeAuditPage> Handle(
        GetAdministrativeAuditEventsQuery request,
        CancellationToken cancellationToken)
    {
        var filter = new AdministrativeAuditFilter
        {
            Action = request.Action,
            AdministratorId = request.AdministratorId,
            MemberId = request.MemberId,
            WishlistId = request.WishlistId,
            ExportId = request.ExportId,
            RequestReference = request.RequestReference?.Trim(),
            From = request.From is null ? null : AdministrativeAuditDate.Parse(request.From),
            To = request.To is null ? null : AdministrativeAuditDate.Parse(request.To),
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 20
        };
        var result = await service.GetPageAsync(
            filter,
            cancellationToken);
        AdministrativeAuditLogMessages.Retrieved(
            logger,
            request.CallerId);

        return result;
    }
}
