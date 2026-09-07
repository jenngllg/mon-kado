using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
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
    IRefreshTokenCookieService refreshTokenCookieService) : ControllerBase
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
    // The OIDC middleware validates one-time state, nonce, correlation and PKCE before this fallback can run.
    // codeql[cs/web/missing-token-validation]
    public IActionResult Callback([FromForm] GoogleOpenIdConnectCallbackRequest? request)
    {
        _ = request;
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        return Redirect(returnPathService.BuildAbsoluteUri(
            GoogleAuthenticationConstants.AuthenticationFailurePath));
    }

    /// <summary>
    /// Finalizes a validated browser flow and creates a MonKado bearer session.
    /// </summary>
    /// <param name="request">The browser-flow proof returned by the Google callback.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bearer access token and a protected refresh cookie.</returns>
    /// <exception cref="GoogleAccountLinkRequiredException">A local password proof is required.</exception>
    /// <exception cref="GoogleAdditionalVerificationRequiredException">Additional identity verification is required.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The flow cannot be completed safely.</exception>
    [HttpPost("completions")]
    [GoogleExternalCookie]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthenticationRateLimitingExtensions.GoogleCompletionPolicy)]
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
    public async Task<ActionResult<AccessTokenResponse>> CompleteAsync(
        CompleteGoogleSessionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        EnsureHttps();
        var tokens = await sender.Send(
            new CompleteGoogleSessionCommand(request.Flow),
            cancellationToken);

        return await CreateSessionResponseAsync(
            tokens,
            cancellationToken);
    }

    /// <summary>
    /// Proves the current MonKado password and explicitly links the validated Google identity.
    /// </summary>
    /// <param name="request">The browser-flow and current password proofs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bearer access token and a protected refresh cookie.</returns>
    [HttpPost("link")]
    [GoogleExternalCookie]
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
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        EnsureHttps();
        var tokens = await sender.Send(
            new LinkGoogleSessionCommand(
                request.Flow,
                request.CurrentPassword),
            cancellationToken);

        return await CreateSessionResponseAsync(
            tokens,
            cancellationToken);
    }

    /// <summary>Publishes only a committed session and clears the completed external cookie.</summary>
    /// <param name="tokens">The confirmed MonKado session tokens.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The access-token response.</returns>
    private async Task<ActionResult<AccessTokenResponse>> CreateSessionResponseAsync(
        AccountSessionTokens tokens,
        CancellationToken cancellationToken)
    {
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
}
