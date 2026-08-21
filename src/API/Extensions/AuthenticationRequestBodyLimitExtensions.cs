namespace JennGllg.Fr.MonKado.Back.Api.Extensions;
/// <summary>
/// Represents authentication request body limit extensions.
/// </summary>

public static class AuthenticationRequestBodyLimitExtensions
{
    private const long MaximumRequestBodySize = 4 * 1024;
    private static readonly PathString _registrationPath = new("/api/v1/auth/registrations");
    private static readonly PathString _confirmationPath = new("/api/v1/auth/email-confirmations");
    private static readonly PathString _loginPath = new("/api/v1/auth/sessions");
    private static readonly PathString _confirmationRequestPath =
        new("/api/v1/auth/email-confirmation-requests");
    /// <summary>
    /// Executes the use authentication request body limits operation.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns>The operation result.</returns>

    public static IApplicationBuilder UseAuthenticationRequestBodyLimits(
        this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Use(async (
            context,
            next) =>
        {

            if (IsLimitedAuthenticationRequest(context.Request) &&
                context.Request.ContentLength > MaximumRequestBodySize)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                context.Response.Headers.CacheControl = "no-store";

                return;
            }

            await next(context);
        });
    }

    private static bool IsLimitedAuthenticationRequest(HttpRequest request)
    {

        return HttpMethods.IsPost(request.Method) &&
            (request.Path == _registrationPath ||
                request.Path == _confirmationPath ||
                request.Path == _confirmationRequestPath ||
                request.Path == _loginPath);
    }
}
