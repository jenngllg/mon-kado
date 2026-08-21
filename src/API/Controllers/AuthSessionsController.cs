using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages authentication sessions.
/// </summary>
[ApiController]
[Route("api/v1/auth/sessions")]
public class AuthSessionsController(
    ISender sender,
    IRefreshTokenCookieService refreshTokenCookieService) : ControllerBase
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
    [ValidateAntiForgeryToken]
    [ProducesResponseType(typeof(AccessTokenResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
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

    internal static AccessTokenResponse CreateResponse(AccountSessionTokens tokens)
    {
        return new AccessTokenResponse(
            tokens.AccessToken.Value,
            "Bearer",
            tokens.AccessToken.ExpiresIn);
    }
}
