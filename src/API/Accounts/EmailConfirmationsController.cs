using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Accounts;

[ApiController]
[Route("api/v1/auth")]
public sealed class EmailConfirmationsController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    [HttpPost("email-confirmations")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.EmailConfirmationPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Confirm(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new ConfirmEmailCommand(request.UserId, request.Token),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpPost("email-confirmation-requests")]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.EmailConfirmationRequestPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> RequestConfirmation(
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
