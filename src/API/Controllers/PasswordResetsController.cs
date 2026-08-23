using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages anonymous account password reset flows.
/// </summary>
/// <param name="sender">The mediator sender.</param>
/// <param name="refreshTokenCookieService">The refresh token cookie service.</param>
[ApiController]
[AllowAnonymous]
[Route("api/v1/auth")]
public class PasswordResetsController(
    ISender sender,
    IRefreshTokenCookieService refreshTokenCookieService) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>
    /// Requests an account password reset email.
    /// </summary>
    /// <param name="request">The password reset email request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An accepted response regardless of account existence or eligibility.</returns>
    [HttpPost("password-reset-requests")]
    [NoStoreResponse(StatusCodes.Status202Accepted)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.PasswordResetRequestPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> RequestAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RequestPasswordResetCommand(request.Email),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Accepted();
    }

    /// <summary>
    /// Resets an account password using a password reset link.
    /// </summary>
    /// <param name="request">The password reset request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An empty response when the password has been reset.</returns>
    [HttpPost("password-resets")]
    [DeletesRefreshTokenCookie]
    [NoStoreResponse(StatusCodes.Status204NoContent)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.PasswordResetPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> ResetAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ResetPasswordCommand(
                request.UserId,
                request.Token,
                request.NewPassword),
            cancellationToken);
        refreshTokenCookieService.Delete(HttpContext);
        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }
}
