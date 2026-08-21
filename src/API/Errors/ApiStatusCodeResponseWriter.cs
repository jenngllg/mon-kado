using JennGllg.Fr.MonKado.Back.Api.Logging;

namespace JennGllg.Fr.MonKado.Back.Api.Errors;
/// <summary>
/// Represents api status code response writer.
/// </summary>

public static class ApiStatusCodeResponseWriter
{
    /// <summary>
    /// Executes the write async operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task WriteAsync(HttpContext context)
    {
        var response = ApiStatusCodeResponseFactory.Create(context.Response.StatusCode);
        var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(typeof(ApiStatusCodeResponseWriter));

        if (response.ErrorCode is not null)
        {
            ApiLogMessages.ExpectedHttpError(
                logger,
                response.StatusCode,
                response.ErrorCode);
        }

        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(
            response,
            context.RequestAborted);
    }
}
