using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Logging;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using System.ComponentModel;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>
/// Manages Google OpenID Connect authentication and explicit account linking.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/auth/google")]
public class GoogleAuthenticationController(
    ISender sender,
    IOptions<GoogleAuthenticationOptions> options,
    IGoogleReturnPathService returnPathService,
    IGoogleExternalAuthenticationService externalAuthenticationService,
    IRefreshSessionService refreshSessionService,
    IRefreshTokenCookieService refreshTokenCookieService,
    ILogger<GoogleAuthenticationController> logger) : ControllerBase
{
    private const int MaximumRequestBodySize = 4 * 1024;
    private readonly GoogleAuthenticationOptions _options = options.Value;

    /// <summary>
    /// Starts Google sign-in with Authorization Code, PKCE, state and nonce.
    /// </summary>
    /// <param name="returnPath">The optional allowlisted relative frontend path.</param>
    /// <param name="rememberMe">Whether the resulting MonKado session should persist for 30 days.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A redirect to Google's authorization endpoint.</returns>
    [HttpGet]
    [RefreshTokenCookie(isRequired: false)]
    [NoStoreResponse(StatusCodes.Status302Found)]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.GoogleChallengePolicy)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<IActionResult> ChallengeAsync(
        [FromQuery] string? returnPath,
        [FromQuery]
        [DefaultValue(false)]
        bool? rememberMe,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        EnsureHttps();
        var resolvedReturnPath = returnPathService.Resolve(returnPath);
        var currentSessionId = await refreshSessionService.ProveCurrentSessionAsync(
            refreshTokenCookieService.GetValue(Request),
            cancellationToken);
        var properties = externalAuthenticationService.CreateChallengeProperties(
            resolvedReturnPath,
            rememberMe.GetValueOrDefault(),
            currentSessionId);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        return Challenge(
            properties,
            GoogleAuthenticationSchemes.OpenIdConnect);
    }

    /// <summary>
    /// Receives Google's form-post callback through the OpenID Connect middleware.
    /// </summary>
    /// <param name="request">The form-post protocol response consumed before MVC executes.</param>
    /// <returns>A redirect produced by the OpenID Connect middleware.</returns>
    [HttpPost("callback")]
    [ReturnsGoogleExternalCookie]
    [NoStoreResponse(StatusCodes.Status302Found)]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.GoogleCallbackPolicy)]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    public IActionResult Callback([FromForm] GoogleOpenIdConnectCallbackRequest? request)
    {
        _ = request;
        Response.Headers.CacheControl = "no-store";

        return Redirect(returnPathService.BuildAbsoluteUri(
            GoogleAuthenticationConstants.AuthenticationFailurePath));
    }

    /// <summary>
    /// Completes a validated Google callback and creates a refresh-only MonKado session.
    /// </summary>
    /// <param name="flow">The opaque binding returned by the validated callback.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A safe frontend redirect without any token or identity claim.</returns>
    [HttpGet("completion")]
    [GoogleExternalCookie]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.GoogleCompletionPolicy)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> CompleteAsync(
        [FromQuery] string? flow,
        CancellationToken cancellationToken)
    {

        if (!_options.Enabled || !Request.IsHttps)
            return RedirectFlowMismatch("DisabledOrInsecureRequest");

        var authentication = await externalAuthenticationService.AuthenticateAsync(
            HttpContext,
            cancellationToken);

        if (authentication is null)
            return await RedirectAuthenticationFailureAsync(
                "InvalidExternalTicket",
                cancellationToken);

        if (!externalAuthenticationService.MatchesFlowBinding(
                authentication.FlowBinding,
                flow))
            return RedirectFlowMismatch("MismatchedFlowBinding");

        var authenticationContext = authentication.Context;

        GoogleAuthenticationResult result;

        try
        {
            result = await sender.Send(
                new CompleteGoogleAuthenticationCommand(
                    authenticationContext.Identity,
                    authenticationContext.IsPersistent,
                    authenticationContext.ReturnPath,
                    authenticationContext.FlowId,
                    authenticationContext.ExpectedMemberId,
                    authenticationContext.CurrentSessionId),
                cancellationToken);
        }
        catch (GoogleAuthenticationFailedException)
        {

            return await RedirectAuthenticationFailureAsync(
                "ApplicationRejected",
                cancellationToken);
        }

        if (result.Outcome == GoogleAuthenticationOutcome.ExplicitLinkRequired)
        {
            Response.Headers.CacheControl = "no-store";

            return Redirect(returnPathService.BuildAbsoluteUri(
                externalAuthenticationService.BuildBoundPath(
                    GoogleAuthenticationConstants.LinkPath,
                    authentication.FlowBinding)));
        }

        if (result.Outcome == GoogleAuthenticationOutcome.AdditionalVerificationRequired)
        {
            Response.Headers.CacheControl = "no-store";

            return Redirect(returnPathService.BuildAbsoluteUri(
                externalAuthenticationService.BuildBoundPath(
                    GoogleAuthenticationConstants.AdditionalVerificationPath,
                    authentication.FlowBinding)));
        }

        if (result.Outcome != GoogleAuthenticationOutcome.SessionCreated ||
            result.Session is null)
            return await RedirectAuthenticationFailureAsync(
                "InvalidCompletionOutcome",
                cancellationToken);

        refreshTokenCookieService.Append(
            HttpContext,
            result.Session);
        await externalAuthenticationService.DeleteAsync(
            HttpContext,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Redirect(returnPathService.BuildAbsoluteUri(authenticationContext.ReturnPath));
    }

    /// <summary>
    /// Proves the current MonKado password and explicitly links the validated Google identity.
    /// </summary>
    /// <param name="request">The current password proof.</param>
    /// <param name="flow">The opaque binding returned by the validated callback.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A bearer access token when the Google account is linked.</returns>
    [HttpPost("link")]
    [GoogleExternalCookie]
    [GoogleFlowBinding]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.GoogleLinkPolicy)]
    [RequestSizeLimit(MaximumRequestBodySize)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(AccessTokenResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<AccessTokenResponse>> LinkAsync(
        LinkGoogleAccountRequest request,
        [FromQuery] string? flow,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        EnsureHttps();
        var authentication = await externalAuthenticationService.AuthenticateAsync(
            HttpContext,
            cancellationToken) ?? throw new GoogleAuthenticationFailedException();

        if (!externalAuthenticationService.MatchesFlowBinding(
                authentication.FlowBinding,
                flow))
            throw new GoogleAccountLinkFailedException();

        var authenticationContext = authentication.Context;

        var tokens = await sender.Send(
            new LinkGoogleAccountCommand(
                authenticationContext.Identity,
                authenticationContext.IsPersistent,
                authenticationContext.ReturnPath,
                authenticationContext.FlowId,
                authenticationContext.ExpectedMemberId,
                authenticationContext.CurrentSessionId,
                request.CurrentPassword),
            cancellationToken);
        refreshTokenCookieService.Append(
            HttpContext,
            tokens);
        await externalAuthenticationService.DeleteAsync(
            HttpContext,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(AuthSessionsController.CreateResponse(tokens));
    }

    /// <summary>
    /// Ensures that Google authentication is enabled.
    /// </summary>
    /// <exception cref="DependencyUnavailableException">Google authentication is disabled.</exception>
    private void EnsureEnabled()
    {

        if (!_options.Enabled)
            throw new DependencyUnavailableException(
                "Google authentication",
                null);
    }

    /// <summary>
    /// Ensures that the current request uses HTTPS.
    /// </summary>
    /// <exception cref="RequestValidationException">The request does not use HTTPS.</exception>
    private void EnsureHttps()
    {

        if (Request.IsHttps)
            return;

        throw new RequestValidationException(
        [
            new ValidationError(
                "scheme",
                "Google authentication requires HTTPS.")
        ]);
    }

    /// <summary>
    /// Clears terminal external authentication state and redirects to the generic failure route.
    /// </summary>
    /// <param name="classification">The bounded failure classification used for logging.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generic authentication failure redirect.</returns>
    /// <exception cref="OperationCanceledException">The operation is canceled.</exception>
    private async Task<IActionResult> RedirectAuthenticationFailureAsync(
        string classification,
        CancellationToken cancellationToken)
    {
        GoogleAuthenticationLogMessages.CompletionFailed(
            logger,
            classification);
        await externalAuthenticationService.DeleteAsync(
            HttpContext,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Redirect(returnPathService.BuildAbsoluteUri(
            GoogleAuthenticationConstants.AuthenticationFailurePath));
    }

    /// <summary>
    /// Redirects an unbound completion without deleting another concurrent flow's cookie.
    /// </summary>
    /// <param name="classification">The bounded failure classification used for logging.</param>
    /// <returns>The generic authentication failure redirect.</returns>
    private RedirectResult RedirectFlowMismatch(string classification)
    {
        GoogleAuthenticationLogMessages.CompletionFailed(
            logger,
            classification);
        Response.Headers.CacheControl = "no-store";

        return Redirect(returnPathService.BuildAbsoluteUri(
            GoogleAuthenticationConstants.AuthenticationFailurePath));
    }

}
