using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

[ApiController]
[Route("_tests/security")]
public class SecurityTestController : ControllerBase
{
    [HttpGet("safe")]
    public IActionResult Get()
    {

        return NoContent();
    }

    [HttpPost("mutate")]
    [ValidateAntiForgeryToken]
    public IActionResult Post()
    {

        return NoContent();
    }

    [HttpGet("bearer")]
    [Authorize]
    public IActionResult GetBearer()
    {

        return NoContent();
    }

    [HttpGet("invalid-query")]
    public ActionResult<int> GetInvalidQuery([FromQuery] int value)
    {

        return Ok(value);
    }

    [HttpGet("empty-binding-error")]
    public ActionResult<string?> GetEmptyBindingError(
        [ModelBinder(BinderType = typeof(EmptyErrorModelBinder))]
        string? value)
    {

        return Ok(value);
    }

    [HttpPost("required-body")]
    public IActionResult PostRequiredBody([FromBody] object body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return NoContent();
    }

    [HttpGet("unavailable")]
    public IActionResult GetUnavailable()
    {
        throw new DependencyUnavailableException(
            $"PostgreSQL {HttpContext.TraceIdentifier}",
            null);
    }
}
