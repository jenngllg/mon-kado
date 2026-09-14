using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.Controllers;

/// <summary>Continues short-lived second-factor sign-ins and operation-bound setup grants.</summary>
/// <param name="sender">The validated application pipeline.</param>
/// <param name="refreshCookie">The refresh cookie writer used only after confirmed sign-in.</param>
[ApiController]
[Route("api/v1/auth/two-factor")]
[Consumes("application/json")]
[RequestSizeLimit(4096)]
[PreserveRefreshCookie]
[EnableRateLimiting(AuthenticationRateLimitingExtensions.TwoFactorContinuationPolicy)]
public class TwoFactorAuthenticationController(
    ISender sender,
    IRefreshTokenCookieService refreshCookie) : ControllerBase
{
    /// <summary>Verifies a TOTP, starts forced recovery replacement or finishes a previously confirmed enrollment.</summary>
    /// <param name="request">The opaque flow and at most one verification code.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A confirmed Bearer response or a replacement challenge without any session token.</returns>
    [HttpPost("completions")]
    [NoStoreResponse(StatusCodes.Status202Accepted)]
    [ValidateAntiForgeryToken]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AccessTokenResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(TwoFactorChallengeResponse), StatusCodes.Status202Accepted, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<AccessTokenResponse>> CompleteAsync(
        TwoFactorCompletionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new CompleteTwoFactorCommand(
                request.Flow,
                request.Code,
                request.RecoveryCode),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        if (result.Challenge is { } challenge)
            return Accepted(challenge);

        var tokens = result.Tokens ?? throw new InvalidOperationException("A completed second factor must return confirmed tokens.");
        refreshCookie.Append(
            HttpContext,
            tokens);

        return Ok(new AccessTokenResponse(
            tokens.AccessToken.Value,
            "Bearer",
            tokens.AccessToken.ExpiresIn));
    }

    /// <summary>Returns candidate authenticator material only for an authorized enrollment or replacement grant.</summary>
    /// <param name="request">The opaque operation-bound flow.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A manual key and an otpauth URI; a management flow additionally requires its original Bearer session.</returns>
    [HttpPost("setup")]
    [ValidateAntiForgeryToken]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TwoFactorSetupResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<TwoFactorSetupResponse>> GetSetupAsync(
        TwoFactorFlowRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new CreateTwoFactorSetupCommand(request.Flow),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>Confirms a candidate authenticator, revokes older credentials and returns new recovery codes once.</summary>
    /// <param name="request">The setup flow and a code from the candidate authenticator.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>New recovery codes; initial or recovery sign-in must subsequently post the same flow to completions without a code.</returns>
    [HttpPost("setup/confirmations")]
    [ValidateAntiForgeryToken]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TwoFactorRecoveryCodesResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<TwoFactorRecoveryCodesResponse>> ConfirmSetupAsync(
        TwoFactorSetupConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new ConfirmTwoFactorSetupCommand(
                request.Flow,
                request.Code),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }

    /// <summary>Consumes a dedicated reauthentication grant to replace all recovery codes and require a new full sign-in.</summary>
    /// <param name="request">The regeneration-only grant.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>New recovery codes after the old sessions and codes are revoked.</returns>
    [HttpPost("recovery-codes/regenerations")]
    [Authorize(Policy = AuthorizationPolicies.CurrentSession)]
    [NoStoreResponse(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TwoFactorRecoveryCodesResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests, "application/json")]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable, "application/json")]
    public async Task<ActionResult<TwoFactorRecoveryCodesResponse>> RegenerateRecoveryCodesAsync(
        TwoFactorFlowRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new RegenerateTwoFactorRecoveryCodesCommand(request.Flow),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }
}
