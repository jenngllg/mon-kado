using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Executes verified erasure requests without exposing a member's private confirmation flow.</summary>
/// <param name="sender">The shared mediator pipeline.</param>
[ApiController]
[PreserveRefreshCookie]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Authorize(Policy = AuthorizationPolicies.EraseMemberData)]
[Route("api/v1/admin/members/{memberId:guid}/erasure-requests")]
public class AdministrativeAccountErasuresController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>Irreversibly erases another account after verification of its support request.</summary>
    /// <remarks>
    /// Requires a technical requestReference and confirmedMemberId matching the route.
    /// The administrator must verify ownership before execution; these fields are not identity evidence.
    /// Success confirms database erasure and durable asynchronous cleanup, not physical cleanup or email delivery.
    /// Administrative self-erasure is forbidden. No If-Match or antiforgery token is required for this Bearer-only route.
    /// Audit retention is six calendar months; the confirmed recipient is retained for at most 24 hours of delivery attempts.
    /// </remarks>
    /// <param name="memberId">The account to erase.</param>
    /// <param name="request">The confirmed target and external reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content after the erasure commit is confirmed.</returns>
    [HttpPost]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.AdministrativeAccountErasurePolicy)]
    [Consumes("application/json")]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [NoStoreResponse(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> ExecuteAsync(
        Guid memberId,
        ExecuteAdministrativeAccountErasureRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ExecuteAdministrativeAccountErasureCommand(
                Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!),
                memberId,
                request.ConfirmedMemberId,
                request.RequestReference),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }
}
