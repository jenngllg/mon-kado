using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages account registrations.
/// </summary>
[ApiController]
[Route("api/v1/auth/registrations")]
public class AuthRegistrationsController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    /// <summary>
    /// Registers an account and schedules its confirmation email.
    /// </summary>
    /// <param name="request">The account registration request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An accepted response when the request has been processed.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.RegistrationPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RegisterAccountCommand(
                request.Email,
                request.Password,
                request.DisplayName),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";

        return Accepted();
    }
}
