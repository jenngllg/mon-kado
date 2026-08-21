using JennGllg.Fr.MonKado.Back.Api.Errors;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents api error handling extensions.
/// </summary>

public static class ApiErrorHandlingExtensions
{
    /// <summary>
    /// Executes the use api error handling operation.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns>The operation result.</returns>
    public static IApplicationBuilder UseApiErrorHandling(this IApplicationBuilder application)
    {
        application.UseExceptionHandler();
        application.UseStatusCodePages(statusCodeContext =>
            ApiStatusCodeResponseWriter.WriteAsync(statusCodeContext.HttpContext));

        return application;
    }
}
