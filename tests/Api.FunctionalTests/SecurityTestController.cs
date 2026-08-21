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
}
