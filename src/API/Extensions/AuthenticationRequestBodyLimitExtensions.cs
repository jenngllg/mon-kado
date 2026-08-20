namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class AuthenticationRequestBodyLimitExtensions
{
    private const long MaximumRequestBodySize = 4 * 1024;
    private static readonly PathString RegistrationPath = new("/api/v1/auth/registrations");
    private static readonly PathString ConfirmationPath = new("/api/v1/auth/email-confirmations");
    private static readonly PathString LoginPath = new("/api/v1/auth/sessions");
    private static readonly PathString ConfirmationRequestPath =
        new("/api/v1/auth/email-confirmation-requests");

    public static IApplicationBuilder UseAuthenticationRequestBodyLimits(
        this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Use(async (context, next) =>
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
        if (!HttpMethods.IsPost(request.Method))
        {
            return false;
        }

        return request.Path == RegistrationPath ||
            request.Path == ConfirmationPath ||
            request.Path == ConfirmationRequestPath ||
            request.Path == LoginPath;
    }
}
