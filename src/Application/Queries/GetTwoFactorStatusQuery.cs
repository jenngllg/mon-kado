using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Reads the current member's non-secret second-factor status.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
public class GetTwoFactorStatusQuery(
    Guid memberId) : IRequest<TwoFactorStatusResponse>
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
}

/// <summary>Reads the current member's non-secret second-factor status.</summary>
/// <param name="twoFactorService">The transactional second-factor service.</param>
/// <param name="logger">The sanitized operation logger.</param>
public class GetTwoFactorStatusQueryHandler(
    ITwoFactorService twoFactorService,
    ILogger<GetTwoFactorStatusQueryHandler> logger) : IRequestHandler<GetTwoFactorStatusQuery, TwoFactorStatusResponse>
{
    /// <summary>Reads the current member's non-secret second-factor status.</summary>
    /// <param name="request">The centrally validated request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The confirmed operation result.</returns>
    public async Task<TwoFactorStatusResponse> Handle(
        GetTwoFactorStatusQuery request,
        CancellationToken cancellationToken)
    {
        TwoFactorLogMessages.OperationStarted(logger);
        var result = await twoFactorService.GetStatusAsync(
            request.MemberId,
            cancellationToken);
        TwoFactorLogMessages.TwoFactorStatusRetrieved(logger);

        return result;
    }
}
