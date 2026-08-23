namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

/// <summary>
/// Enforces request body limits before endpoints read their content.
/// </summary>
public static class RequestBodyLimitExtensions
{
    private const long MaximumRequestBodySize = 4 * 1024;
    private static readonly PathString _registrationPath = new("/api/v1/auth/registrations");
    private static readonly PathString _confirmationPath = new("/api/v1/auth/email-confirmations");
    private static readonly PathString _loginPath = new("/api/v1/auth/sessions");
    private static readonly PathString _confirmationRequestPath =
        new("/api/v1/auth/email-confirmation-requests");
    private static readonly PathString _emailChangeConfirmationPath =
        new("/api/v1/auth/email-change-confirmations");
    private static readonly PathString _memberProfilePath =
        new("/api/v1/members/current/profile");
    private static readonly PathString _memberEmailPath =
        new("/api/v1/members/current/email");
    private static readonly PathString _memberPasswordPath =
        new("/api/v1/members/current/password");

    /// <summary>
    /// Enforces the request body limit for bounded JSON endpoints.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The application builder.</returns>
    public static IApplicationBuilder UseRequestBodyLimits(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Use(async (
            context,
            next) =>
        {

            if (IsLimitedRequest(context.Request) &&
                context.Request.ContentLength > MaximumRequestBodySize)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                context.Response.Headers.CacheControl = "no-store";

                return;
            }

            await next(context);
        });
    }

    private static bool IsLimitedRequest(HttpRequest request)
    {

        return (HttpMethods.IsPost(request.Method) &&
            (request.Path == _registrationPath ||
                request.Path == _confirmationPath ||
                request.Path == _confirmationRequestPath ||
                request.Path == _emailChangeConfirmationPath ||
                request.Path == _loginPath)) ||
            (HttpMethods.IsPut(request.Method) &&
                (request.Path == _memberProfilePath ||
                    request.Path == _memberEmailPath ||
                    request.Path == _memberPasswordPath));
    }
}
