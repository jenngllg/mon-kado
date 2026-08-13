using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Accounts;

[ApiController]
[Route("api/v1/auth/registrations")]
public sealed class AuthRegistrationsController(ISender sender) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;

    [HttpPost]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.RegistrationPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Register(
        RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        await sender.Send(
            new RegisterAccountCommand(request.Email, request.Password, request.DisplayName),
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
        return Accepted();
    }
}
