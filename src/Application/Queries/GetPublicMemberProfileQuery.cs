using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Requests the public profile of a confirmed member.</summary>
/// <param name="memberId">The member identifier.</param>
public class GetPublicMemberProfileQuery(Guid memberId) : IRequest<PublicMemberProfile>
{
    /// <summary>Gets the member identifier.</summary>
    public Guid MemberId { get; } = memberId;
}

/// <summary>Coordinates public profile reads without logging names or bearer links.</summary>
/// <param name="service">The public profile service.</param>
/// <param name="logger">The structured logger.</param>
public class GetPublicMemberProfileQueryHandler(
    IPublicMemberProfileService service,
    ILogger<GetPublicMemberProfileQueryHandler> logger) : IRequestHandler<GetPublicMemberProfileQuery, PublicMemberProfile>
{
    /// <summary>Reads a validated public profile.</summary>
    /// <param name="request">The validated query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public member profile.</returns>
    /// <exception cref="PublicMemberProfileNotFoundException">The member is absent or unconfirmed.</exception>
    public async Task<PublicMemberProfile> Handle(
        GetPublicMemberProfileQuery request,
        CancellationToken cancellationToken)
    {
        var profile = await service.GetAsync(
            request.MemberId,
            cancellationToken) ?? throw new PublicMemberProfileNotFoundException();
        ApplicationLogMessages.PublicMemberProfileRetrieved(
            logger,
            request.MemberId);

        return profile;
    }
}
