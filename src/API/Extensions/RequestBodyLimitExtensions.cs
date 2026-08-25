using JennGllg.Fr.MonKado.Back.Api.Errors;

using Microsoft.AspNetCore.Http.Features;

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
    private static readonly PathString _passwordResetRequestPath =
        new("/api/v1/auth/password-reset-requests");
    private static readonly PathString _passwordResetPath =
        new("/api/v1/auth/password-resets");
    private static readonly PathString _googleLinkPath =
        new("/api/v1/auth/google/link");
    private static readonly PathString _googleCallbackPath =
        new("/api/v1/auth/google/callback");
    private static readonly PathString _memberProfilePath =
        new("/api/v1/members/current/profile");
    private static readonly PathString _memberEmailPath =
        new("/api/v1/members/current/email");
    private static readonly PathString _memberPasswordPath =
        new("/api/v1/members/current/password");
    private static readonly PathString _wishlistsPath = new("/api/v1/wishlists");

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
            var isLimitedRequest = IsLimitedRequest(context.Request);

            if (isLimitedRequest)
            {
                var maximumBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

                if (maximumBodySizeFeature is not null && !maximumBodySizeFeature.IsReadOnly)
                    maximumBodySizeFeature.MaxRequestBodySize = MaximumRequestBodySize;
            }

            if (isLimitedRequest &&
                context.Request.ContentLength > MaximumRequestBodySize)
            {
                await WritePayloadTooLargeAsync(
                    context,
                    context.RequestAborted);

                return;
            }

            if (isLimitedRequest &&
                context.Request.ContentLength is null &&
                await ExceedsMaximumBodySizeAsync(
                    context.Request,
                    context.RequestAborted))
            {
                await WritePayloadTooLargeAsync(
                    context,
                    context.RequestAborted);

                return;
            }

            await next(context);
        });
    }

    /// <summary>
    /// Reads at most one byte beyond the configured limit without consuming a valid request body.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the request body exceeds the limit.</returns>
    /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
    private static async Task<bool> ExceedsMaximumBodySizeAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        request.EnableBuffering(
            bufferThreshold: (int)MaximumRequestBodySize + 1,
            bufferLimit: MaximumRequestBodySize + 1);
        var buffer = new byte[MaximumRequestBodySize + 1];
        var totalBytesRead = 0;

        try
        {
            while (totalBytesRead < buffer.Length)
            {
                var bytesRead = await request.Body.ReadAsync(
                    buffer.AsMemory(totalBytesRead),
                    cancellationToken);

                if (bytesRead == 0)
                    break;

                totalBytesRead += bytesRead;
            }
        }
        catch (BadHttpRequestException exception)
            when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {

            return true;
        }
        finally
        {

            if (request.Body.CanSeek)
                request.Body.Position = 0;
        }

        return totalBytesRead > MaximumRequestBodySize;
    }

    /// <summary>
    /// Writes the shared structured payload-too-large response.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
    private static async Task WritePayloadTooLargeAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.Headers.CacheControl = "no-store";
        await ApiStatusCodeResponseWriter.WriteAsync(
            context,
            cancellationToken);
    }

    /// <summary>
    /// Determines whether the request targets an endpoint with a bounded body.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <returns><see langword="true" /> when the request must be bounded.</returns>
    private static bool IsLimitedRequest(HttpRequest request)
    {

        return (HttpMethods.IsPost(request.Method) &&
            (MatchesPath(request.Path, _registrationPath) ||
                MatchesPath(request.Path, _confirmationPath) ||
                MatchesPath(request.Path, _confirmationRequestPath) ||
                MatchesPath(request.Path, _emailChangeConfirmationPath) ||
                MatchesPath(request.Path, _passwordResetRequestPath) ||
                MatchesPath(request.Path, _passwordResetPath) ||
                MatchesPath(request.Path, _googleLinkPath) ||
                MatchesPath(request.Path, _googleCallbackPath) ||
                MatchesPath(request.Path, _loginPath) ||
                MatchesPath(request.Path, _wishlistsPath) ||
                MatchesWishCollectionPath(request.Path))) ||
            (HttpMethods.IsPut(request.Method) &&
                (MatchesPath(request.Path, _memberProfilePath) ||
                    MatchesPath(request.Path, _memberEmailPath) ||
                    MatchesPath(request.Path, _memberPasswordPath) ||
                    MatchesWishlistResourcePath(request.Path)));
    }

    /// <summary>
    /// Matches a wishlist resource endpoint and its equivalent route with one trailing slash.
    /// </summary>
    /// <param name="requestPath">The request path.</param>
    /// <returns><see langword="true" /> when the path identifies a wishlist resource.</returns>
    private static bool MatchesWishlistResourcePath(PathString requestPath)
    {

        if (!requestPath.StartsWithSegments(
            _wishlistsPath,
            out var remainingPath))
            return false;

        var remainingValue = remainingPath.Value;

        if (string.IsNullOrEmpty(remainingValue) || remainingValue[0] != '/')
            return false;

        var wishlistId = remainingValue.AsSpan(1);

        if (wishlistId.EndsWith('/'))
            wishlistId = wishlistId[..^1];

        return !wishlistId.IsEmpty &&
            !wishlistId.Contains('/') &&
            Guid.TryParse(
                wishlistId,
                out _);
    }

    /// <summary>
    /// Matches a nested wish collection endpoint and its equivalent route with one trailing slash.
    /// </summary>
    /// <param name="requestPath">The request path.</param>
    /// <returns><see langword="true" /> when the path identifies a wish collection.</returns>
    private static bool MatchesWishCollectionPath(PathString requestPath)
    {

        if (!requestPath.StartsWithSegments(
            _wishlistsPath,
            out var remainingPath))
        {
            return false;
        }

        var nestedPath = remainingPath.Value.AsSpan(1);

        if (nestedPath.EndsWith('/'))
            nestedPath = nestedPath[..^1];

        var separatorIndex = nestedPath.IndexOf('/');

        if (separatorIndex <= 0)
            return false;

        var wishlistId = nestedPath[..separatorIndex];
        var resourceName = nestedPath[(separatorIndex + 1)..];

        return resourceName.Equals(
                "wishes",
                StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(
                wishlistId,
                out _);
    }

    /// <summary>
    /// Matches the canonical endpoint path and the equivalent route with one trailing slash.
    /// </summary>
    /// <param name="requestPath">The request path.</param>
    /// <param name="endpointPath">The configured endpoint path.</param>
    /// <returns><see langword="true" /> when both paths identify the same endpoint.</returns>
    private static bool MatchesPath(
        PathString requestPath,
        PathString endpointPath)
    {

        return requestPath == endpointPath ||
            requestPath == endpointPath.Add(new PathString("/"));
    }
}
