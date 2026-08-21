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
/// Manages account email confirmation.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class EmailConfirmationsController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>
    /// Confirms an account email address.
    /// </summary>
    /// <param name="request">The confirmation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An empty response when confirmation succeeds.</returns>
    [HttpPost("email-confirmations")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.EmailConfirmationPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest,
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
    public async Task<IActionResult> ConfirmAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ConfirmEmailCommand(
                request.UserId,
                request.Token),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return NoContent();
    }

    /// <summary>
    /// Requests a new account confirmation email.
    /// </summary>
    /// <param name="request">The confirmation email request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An accepted response regardless of account existence.</returns>
    [HttpPost("email-confirmation-requests")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.EmailConfirmationRequestPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest,
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
    public async Task<IActionResult> RequestConfirmationAsync(
        RequestEmailConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RequestEmailConfirmationCommand(request.Email),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return Accepted();
    }
}
