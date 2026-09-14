using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Logging;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;

namespace JennGllg.Fr.MonKado.Back.Api.Filters;

/// <summary>Normalizes rejected antiforgery results before MVC maps them to ProblemDetails.</summary>
/// <param name="logger">The structured API error logger.</param>
public class AntiforgeryErrorResponseFilter(ILogger<AntiforgeryErrorResponseFilter> logger) : IAsyncAlwaysRunResultFilter
{
    /// <summary>Preserves antiforgery rejection while returning the public API error contract.</summary>
    /// <param name="context">The result about to execute, including authorization short circuits.</param>
    /// <param name="next">The remaining result pipeline.</param>
    /// <returns>A task completing after the result pipeline.</returns>
    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {

        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new ObjectResult(new ErrorResponse(
                StatusCodes.Status400BadRequest,
                "Bad request",
                "The antiforgery token is missing or invalid.",
                ErrorCodes.SecurityCsrfValidationFailed,
                null))
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
            ApiLogMessages.ExpectedHttpError(
                logger,
                StatusCodes.Status400BadRequest,
                ErrorCodes.SecurityCsrfValidationFailed);
        }
        await next();
    }
}
