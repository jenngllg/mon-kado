using Microsoft.AspNetCore.Mvc;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

[ApiController]
[Route("_tests/security")]
public sealed class SecurityTestController : ControllerBase
{
    [HttpGet("safe")]
    public IActionResult Get()
    {
        return NoContent();
    }

    [HttpPost("mutate")]
    public IActionResult Post()
    {
        return NoContent();
    }
}
