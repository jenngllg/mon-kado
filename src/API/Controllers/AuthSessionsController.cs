using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages authentication sessions.
/// </summary>
[ApiController]
[Route("api/v1/auth/sessions")]
public class AuthSessionsController(
    ISender sender,
    IRefreshTokenCookieService refreshTokenCookieService,
    IEntityTagService entityTagService) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>
    /// Authenticates an account and creates its server-side session.
    /// </summary>
    /// <param name="request">The login request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A bearer access token when authentication succeeds.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.LoginPolicy)]
    [RefreshTokenCookie(isRequired: false)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AccessTokenResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<AccessTokenResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var tokens = await sender.Send(
            new LoginCommand(
                request.Email,
                request.Password,
                request.RememberMe,
                refreshTokenCookieService.GetValue(Request)),
            cancellationToken);

        refreshTokenCookieService.Append(
            HttpContext,
            tokens);
        Response.Headers.CacheControl = "no-store";

        return Ok(CreateResponse(tokens));
    }

    /// <summary>
    /// Rotates the current refresh session and issues a new access token.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A new bearer access token.</returns>
    [HttpPost("refresh")]
    [RefreshTokenCookie]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.RefreshPolicy)]
    [ProducesResponseType(typeof(AccessTokenResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<AccessTokenResponse>> RefreshAsync(
        CancellationToken cancellationToken)
    {
        var tokens = await sender.Send(
            new RefreshSessionCommand(refreshTokenCookieService.GetValue(Request)),
            cancellationToken);

        refreshTokenCookieService.Append(
            HttpContext,
            tokens);
        Response.Headers.CacheControl = "no-store";

        return Ok(CreateResponse(tokens));
    }

    /// <summary>
    /// Gets the current authenticated member session from persistence.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current member identity and roles.</returns>
    [HttpGet("current")]
    [Authorize(Policy = AuthorizationPolicies.CurrentSession)]
    [EntityTag]
    [ProducesResponseType(typeof(CurrentSessionResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<CurrentSessionResponse>> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        _ = Guid.TryParse(
            subject,
            out var memberId);
        var currentSession = await sender.Send(
            new GetCurrentSessionQuery(memberId),
            cancellationToken);
        var response = new CurrentSessionResponse(
            currentSession.Id,
            currentSession.Email,
            currentSession.DisplayName,
            currentSession.Roles);
        Response.Headers.ETag = entityTagService.Format(currentSession.Version);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>
    /// Ends the current browser refresh session.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content after the browser session is cleared.</returns>
    [HttpDelete("current")]
    [AllowAnonymous]
    [RefreshTokenCookie(isRequired: false)]
    [DeletesRefreshTokenCookie]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.RefreshPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> LogoutAsync(CancellationToken cancellationToken)
    {
        await sender.Send(
            new LogoutCommand(refreshTokenCookieService.GetValue(Request)),
            cancellationToken);
        refreshTokenCookieService.Delete(HttpContext);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }

    /// <summary>
    /// Creates the public access token response from issued session tokens.
    /// </summary>
    /// <param name="tokens">The issued session tokens.</param>
    /// <returns>The public bearer token response.</returns>
    internal static AccessTokenResponse CreateResponse(AccountSessionTokens tokens)
    {

        return new AccessTokenResponse(
            tokens.AccessToken.Value,
            "Bearer",
            tokens.AccessToken.ExpiresIn);
    }
}
