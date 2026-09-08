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

/// <summary>Revokes all sessions of another member using database-backed administrative authorization.</summary>
/// <param name="sender">The shared mediator pipeline.</param>
[ApiController]
[PreserveRefreshCookie]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Authorize(Policy = AuthorizationPolicies.RevokeMemberSessions)]
[Route("api/v1/admin/members/{memberId:guid}/session-revocations")]
public class AdministrativeSessionRevocationsController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;
    /// <summary>Revokes another account's sessions immediately after explicit target confirmation.</summary>
    /// <remarks>
    /// Requires a technical requestReference and confirmedMemberId matching the route.
    /// The reference identifies the administrative request; it is not an idempotency key.
    /// Success confirms session revocation and its audit; later connections remain allowed.
    /// Administrative self-revocation is forbidden. No If-Match or antiforgery token is required for this Bearer-only route.
    /// Audit retention is six calendar months. No email is sent and no cookie is modified.
    /// </remarks>
    /// <param name="memberId">The account to disconnect.</param>
    /// <param name="request">The confirmed target and external reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content after the revocation commit is confirmed.</returns>
    [HttpPost]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.AdministrativeSessionRevocationPolicy)]
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
        RevokeMemberSessionsRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RevokeMemberSessionsCommand(
                Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!),
                memberId,
                request.ConfirmedMemberId,
                request.RequestReference),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }
}
