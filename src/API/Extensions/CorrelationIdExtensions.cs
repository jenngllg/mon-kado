using JennGllg.Fr.MonKado.Back.Api.Middleware;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents correlation id extensions.
/// </summary>

public static class CorrelationIdExtensions
{
    /// <summary>
    /// Executes the use correlation id operation.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns>The operation result.</returns>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder application)
    {
        application.UseMiddleware<CorrelationIdMiddleware>();

        return application;
    }
}
