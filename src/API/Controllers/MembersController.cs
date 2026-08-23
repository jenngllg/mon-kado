using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages authenticated members.
/// </summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="entityTagService">The entity tag service.</param>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.CurrentSession)]
[Route("api/v1/members")]
public class MembersController(
    ISender sender,
    IEntityTagService entityTagService) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>
    /// Updates the display name of the current authenticated member.
    /// </summary>
    /// <param name="request">The profile update request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated member profile.</returns>
    [HttpPut("current/profile")]
    [EntityTag(isRequired: true)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(MemberProfileResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<MemberProfileResponse>> UpdateProfileAsync(
        UpdateMemberProfileRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var memberId);
        var expectedVersion = entityTagService.Parse(Request.Headers.IfMatch);
        var profile = await sender.Send(
            new UpdateMemberProfileCommand(
                memberId,
                request.DisplayName,
                expectedVersion),
            cancellationToken);
        var response = new MemberProfileResponse(profile.DisplayName);
        Response.Headers.ETag = entityTagService.Format(profile.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>
    /// Requests a change to the current authenticated member email address.
    /// </summary>
    /// <param name="request">The member email change request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An accepted response when the request has been processed.</returns>
    [HttpPut("current/email")]
    [EntityTag(isRequired: true, returnsEntityTag: false)]
    [NoStoreResponse(StatusCodes.Status202Accepted)]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.EmailChangeRequestPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status412PreconditionFailed, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status428PreconditionRequired, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> UpdateEmailAsync(
        UpdateMemberEmailRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var memberId);
        var expectedVersion = entityTagService.Parse(Request.Headers.IfMatch);
        await sender.Send(
            new RequestMemberEmailChangeCommand(
                memberId,
                request.Email,
                request.CurrentPassword,
                expectedVersion),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Accepted();
    }
}
