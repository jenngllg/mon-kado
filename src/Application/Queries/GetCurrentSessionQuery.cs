using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>
/// Represents a request for the current authenticated member session.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
public class GetCurrentSessionQuery(Guid memberId)
    : IRequest<CurrentSession>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated member identifier.
    /// </summary>
    public Guid MemberId { get; } = memberId;

    Exception IGenericValidationFailure.CreateValidationException()
    {

        return new InvalidAuthenticationSessionException();
    }
}

/// <summary>
/// Handles current authenticated member session queries.
/// </summary>
/// <param name="currentSessionService">The current session service.</param>
/// <param name="logger">The logger.</param>
public class GetCurrentSessionQueryHandler(
    ICurrentSessionService currentSessionService,
    ILogger<GetCurrentSessionQueryHandler> logger)
    : IRequestHandler<GetCurrentSessionQuery, CurrentSession>
{
    /// <summary>
    /// Gets the current authenticated member session.
    /// </summary>
    /// <param name="request">The current session query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current member session.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member no longer exists.</exception>
    public async Task<CurrentSession> Handle(
        GetCurrentSessionQuery request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.CurrentSessionRetrievalStarted(
            logger,
            request.MemberId);
        var currentSession = await currentSessionService.GetAsync(
            request.MemberId,
            cancellationToken);

        if (currentSession is null)
            throw new InvalidAuthenticationSessionException();

        ApplicationLogMessages.CurrentSessionRetrieved(
            logger,
            request.MemberId);

        return currentSession;
    }
}
