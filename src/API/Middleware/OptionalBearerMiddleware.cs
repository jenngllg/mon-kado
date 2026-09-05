using JennGllg.Fr.MonKado.Back.Api.Attributes;

using Microsoft.Net.Http.Headers;

namespace JennGllg.Fr.MonKado.Back.Api.Middleware;

/// <summary>
/// Rejects invalid credentials on endpoints where Bearer authentication is optional.
/// </summary>
/// <param name="next">The next request delegate.</param>
public class OptionalBearerMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Processes an optionally authenticated request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var acceptsOptionalBearer = context
            .GetEndpoint()?
            .Metadata
            .GetMetadata<OptionalBearerAttribute>() is not null;

        if (!acceptsOptionalBearer ||
            !context.Request.Headers.ContainsKey(HeaderNames.Authorization) ||
            context.User.Identity?.IsAuthenticated is true)
        {
            await next(context);

            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }
}
