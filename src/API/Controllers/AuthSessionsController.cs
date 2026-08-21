using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages authentication sessions.
/// </summary>
[ApiController]
[Route("api/v1/auth/sessions")]
public class AuthSessionsController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>
    /// Authenticates an account and creates its server-side session.
    /// </summary>
    /// <param name="request">The login request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An empty response when authentication succeeds.</returns>
    [HttpPost]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.LoginPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest,
        "application/json")]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status401Unauthorized,
        "application/json")]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status429TooManyRequests,
        "application/json")]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status503ServiceUnavailable,
        "application/json")]
    public async Task<IActionResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new LoginCommand(
                request.Email,
                request.Password,
                request.RememberMe),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }
}
