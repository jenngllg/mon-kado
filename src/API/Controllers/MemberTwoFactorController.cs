using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Reads authenticator status and proves the current factor before management operations.</summary>
/// <param name="sender">The centrally validated application pipeline.</param>
/// <param name="callerProvider">The validated Bearer identity.</param>
[ApiController]
[Route("api/v1/members/current/two-factor")]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[PreserveRefreshCookie]
public class MemberTwoFactorController(
    ISender sender,
    ITwoFactorCallerProvider callerProvider) : ControllerBase
{
    /// <summary>Retrieves current authenticator enrollment and the remaining recovery-code count without secrets.</summary>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The non-secret enrollment status.</returns>
    [HttpGet]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TwoFactorStatusResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<TwoFactorStatusResponse>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var caller = callerProvider.GetCurrent() ?? throw new InvalidAccessTokenException();
        var response = await sender.Send(
            new GetTwoFactorStatusQuery(caller.MemberId),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>Verifies the current factor to authorize either authenticator replacement or recovery-code regeneration.</summary>
    /// <param name="request">The requested purpose and exactly one current-factor proof.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A five-minute operation-bound grant; a recovery code authorizes replacement only.</returns>
    [HttpPost("reauthentications")]
    [StartsTwoFactorChallenge]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.TwoFactorReauthenticationPolicy)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TwoFactorChallengeResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<TwoFactorChallengeResponse>> ReauthenticateAsync(
        TwoFactorReauthenticationRequest request,
        CancellationToken cancellationToken)
    {
        var caller = callerProvider.GetCurrent() ?? throw new InvalidAccessTokenException();
        var response = await sender.Send(
            new ReauthenticateTwoFactorCommand(
                caller.MemberId,
                caller.AccessTokenId,
                request.Purpose,
                request.Code,
                request.RecoveryCode),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }
}
