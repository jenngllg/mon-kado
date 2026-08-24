using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Logging;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using MediatR;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

using System.Buffers;
using System.Globalization;
using System.Security.Claims;
using System.Text;

using JwtRegisteredClaimNames = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames;
using JwtSecurityToken = System.IdentityModel.Tokens.Jwt.JwtSecurityToken;

namespace JennGllg.Fr.MonKado.Back.Api.Handlers;

/// <summary>
/// Restricts Google OpenID Connect state and converts every remote failure to a safe redirect.
/// </summary>
public class GoogleOpenIdConnectEvents(
    ILogger<GoogleOpenIdConnectEvents> logger,
    IGoogleReturnPathService returnPathService,
    IGoogleExternalAuthenticationService externalAuthenticationService,
    IOptions<GoogleAuthenticationOptions> options,
    TimeProvider timeProvider,
    ISender sender) : OpenIdConnectEvents
{
    private const string AuthorizedPartyClaim = "azp";
    private const string EmailClaim = "email";
    private const string EmailVerifiedClaim = "email_verified";
    private const string HostedDomainClaim = "hd";
    private const string NameClaim = "name";
    /// <summary>
    /// Rejects callback transports other than an exact form-urlencoded POST.
    /// </summary>
    /// <param name="context">The received protocol message context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override Task MessageReceived(MessageReceivedContext context)
    {
        var hasExpectedMediaType = MediaTypeHeaderValue.TryParse(
                context.Request.ContentType,
                out var contentType) &&
            string.Equals(
                contentType.MediaType.ToString(),
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase);

        if (context.Request.IsHttps &&
            HttpMethods.IsPost(context.Request.Method) &&
            hasExpectedMediaType)
            return Task.CompletedTask;

        GoogleAuthenticationLogMessages.ProtocolFailed(
            logger,
            "InvalidCallbackTransport");
        context.HandleResponse();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(returnPathService.BuildAbsoluteUri(
            GoogleAuthenticationConstants.AuthenticationFailurePath));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Rejects expired protected state before the authorization code reaches Google's token endpoint.
    /// </summary>
    /// <param name="context">The authorization-code receipt context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public override Task AuthorizationCodeReceived(AuthorizationCodeReceivedContext context)
    {

        if (context.Properties?.ExpiresUtc is DateTimeOffset expiresAt &&
            expiresAt > timeProvider.GetUtcNow())
            return Task.CompletedTask;

        GoogleAuthenticationLogMessages.ProtocolFailed(
            logger,
            "ExpiredRemoteFlow");
        context.HandleResponse();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(returnPathService.BuildAbsoluteUri(
            GoogleAuthenticationConstants.AuthenticationFailurePath));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds the account selector to every outbound Google authorization request.
    /// </summary>
    /// <param name="context">The redirect context.</param>
    /// <returns>A completed task.</returns>
    public override Task RedirectToIdentityProvider(RedirectContext context)
    {
        context.ProtocolMessage.Prompt = "select_account";
        GoogleAuthenticationLogMessages.ChallengeStarted(logger);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces the validated principal with the minimal claims required by MonKado.
    /// </summary>
    /// <param name="context">The token validation context.</param>
    /// <returns>A completed task.</returns>
    public override Task TokenValidated(TokenValidatedContext context)
    {
        var source = context.Principal;
        var properties = context.Properties;

        if (source is null)
        {
            context.Fail("The validated Google identity is incomplete.");

            return Task.CompletedTask;
        }

        var audiences = source
            .FindAll(JwtRegisteredClaimNames.Aud)
            .Select(claim => claim.Value)
            .ToArray();

        if (!HasValidatedRsaSignature(context.SecurityToken) ||
            !TryGetSingleClaim(
                source,
                JwtRegisteredClaimNames.Sub,
                out var subject) ||
            string.IsNullOrEmpty(subject) ||
            subject.Length > GoogleIdentityValidator.MaximumSubjectLength ||
            subject.Any(character => character is < '!' or > '~') ||
            !TryGetSingleClaim(
                source,
                EmailClaim,
                out var email) ||
            string.IsNullOrWhiteSpace(email) ||
            !TryGetSingleClaim(
                source,
                EmailVerifiedClaim,
                out var emailVerified) ||
            !bool.TryParse(
                emailVerified,
                out var verified) ||
            !verified ||
            !TryGetSingleClaim(
                source,
                JwtRegisteredClaimNames.Iat,
                out var issuedAt) ||
            !IsIssuedAtValid(issuedAt) ||
            !TryGetOptionalSingleClaim(
                source,
                HostedDomainClaim,
                out var hostedDomain) ||
            !TryGetOptionalSingleClaim(
                source,
                NameClaim,
                out var displayName) ||
            !TryGetOptionalSingleClaim(
                source,
                AuthorizedPartyClaim,
                out var authorizedParty) ||
            audiences.Length == 0 ||
            !audiences.Contains(
                options.Value.ClientId,
                StringComparer.Ordinal) ||
            (audiences.Length > 1 && authorizedParty is null) ||
            (authorizedParty is not null &&
                !string.Equals(
                    authorizedParty,
                    options.Value.ClientId,
                    StringComparison.Ordinal)))
        {
            context.Fail("The validated Google identity is incomplete.");

            return Task.CompletedTask;
        }

        displayName = NormalizeDisplayName(displayName);

        var claims = new List<Claim>
        {
            new(
                JwtRegisteredClaimNames.Sub,
                subject),
            new(
                EmailClaim,
                email),
            new(
                EmailVerifiedClaim,
                bool.TrueString.ToLowerInvariant())
        };

        AddOptionalClaim(
            claims,
            HostedDomainClaim,
            hostedDomain);
        AddOptionalClaim(
            claims,
            NameClaim,
            displayName);
        var identity = new ClaimsIdentity(
            claims,
            GoogleAuthenticationSchemes.ExternalCookie,
            NameClaim,
            ClaimTypes.Role);
        context.Principal = new ClaimsPrincipal(identity);
        context.Response.Headers.CacheControl = "no-store";

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the expected member only after nonce and protocol response validation succeeded.
    /// </summary>
    /// <param name="context">The ticket receipt context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">The request is canceled.</exception>
    public override async Task TicketReceived(TicketReceivedContext context)
    {
        var principal = context.Principal;
        var properties = context.Properties;

        if (principal is null ||
            properties is null ||
            !TryCreateIdentity(
                principal,
                out var identity))
        {
            GoogleAuthenticationLogMessages.ProtocolFailed(
                logger,
                "InvalidValidatedIdentity");
            await RedirectTicketFailureAsync(
                context,
                GoogleAuthenticationConstants.AuthenticationFailurePath);

            return;
        }

        Guid? expectedMemberId;

        try
        {
            expectedMemberId = await sender.Send(
                new ResolveGoogleExpectedMemberCommand(identity),
                context.HttpContext.RequestAborted);
        }
        catch (DependencyUnavailableException exception)
        {
            GoogleAuthenticationLogMessages.ExpectedMemberResolutionUnavailable(
                logger,
                exception);
            await RedirectTicketFailureAsync(
                context,
                GoogleAuthenticationConstants.AuthenticationUnavailablePath);

            return;
        }
        catch (GoogleAuthenticationFailedException)
        {
            GoogleAuthenticationLogMessages.ProtocolFailed(
                logger,
                nameof(GoogleAuthenticationFailedException));
            await RedirectTicketFailureAsync(
                context,
                GoogleAuthenticationConstants.AuthenticationFailurePath);

            return;
        }

        var flowBinding = externalAuthenticationService.CreateFlowBinding();
        MinimizeProtectedProperties(
            properties,
            flowBinding);
        context.ReturnUri = properties.RedirectUri;
        properties.Items[GoogleAuthenticationConstants.ExpectedMemberIdProperty] =
            expectedMemberId.HasValue
                ? expectedMemberId.Value.ToString("D")
                : GoogleAuthenticationConstants.NoExpectedMemberValue;
        context.Response.Headers.CacheControl = "no-store";
        GoogleAuthenticationLogMessages.IdentityValidated(logger);
    }

    /// <summary>
    /// Ends a failed validated-ticket flow without issuing an external identity cookie.
    /// </summary>
    /// <param name="context">The ticket receipt context.</param>
    /// <param name="frontendPath">The fixed frontend failure path.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private Task RedirectTicketFailureAsync(
        TicketReceivedContext context,
        string frontendPath)
    {
        context.HandleResponse();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(returnPathService.BuildAbsoluteUri(
            frontendPath));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Converts all remote provider failures into the same non-sensitive frontend redirect.
    /// </summary>
    /// <param name="context">The remote failure context.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="OperationCanceledException">The request is canceled.</exception>
    public override async Task RemoteFailure(RemoteFailureContext context)
    {
        var dependencyException = FindDependencyUnavailableException(context.Failure);

        if (dependencyException is not null)
        {
            GoogleAuthenticationLogMessages.ProviderUnavailable(
                logger,
                "ProviderConfiguration");
            await RedirectFailureAsync(
                context,
                GoogleAuthenticationConstants.AuthenticationUnavailablePath);

            return;
        }

        var transportFailureType = FindProviderTransportFailureType(
            context.Failure,
            context.HttpContext.RequestAborted.IsCancellationRequested);

        if (transportFailureType is not null)
        {
            GoogleAuthenticationLogMessages.ProviderUnavailable(
                logger,
                transportFailureType);
            await RedirectFailureAsync(
                context,
                GoogleAuthenticationConstants.AuthenticationUnavailablePath);

            return;
        }

        var failureType = context.Failure?.GetType().Name ?? "UnknownRemoteFailure";
        GoogleAuthenticationLogMessages.ProtocolFailed(
            logger,
            failureType);
        await RedirectFailureAsync(
            context,
            GoogleAuthenticationConstants.AuthenticationFailurePath);
    }

    /// <summary>
    /// Ends a remote authentication failure with a safe frontend redirect.
    /// </summary>
    /// <param name="context">The remote failure context.</param>
    /// <param name="frontendPath">The fixed frontend failure path.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    private Task RedirectFailureAsync(
        RemoteFailureContext context,
        string frontendPath)
    {
        context.HandleResponse();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Redirect(returnPathService.BuildAbsoluteUri(
            frontendPath));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds a classified dependency failure in an exception chain.
    /// </summary>
    /// <param name="exception">The outer exception.</param>
    /// <returns>The classified dependency failure, or <see langword="null" />.</returns>
    private static DependencyUnavailableException? FindDependencyUnavailableException(
        Exception? exception)
    {
        var current = exception;

        while (current is not null)
        {

            if (current is DependencyUnavailableException dependencyException)
                return dependencyException;

            current = current.InnerException;
        }

        return null;
    }

    /// <summary>
    /// Classifies a provider transport failure without exposing provider content.
    /// </summary>
    /// <param name="exception">The outer exception.</param>
    /// <param name="requestAborted">Whether the client canceled the current request.</param>
    /// <returns>The bounded transport classification, or <see langword="null" />.</returns>
    private static string? FindProviderTransportFailureType(
        Exception? exception,
        bool requestAborted)
    {

        if (requestAborted)
            return null;

        var current = exception;

        while (current is not null)
        {

            if (current is HttpRequestException)
                return nameof(HttpRequestException);

            if (current is TaskCanceledException or TimeoutException)
                return "BackchannelTimeout";

            current = current.InnerException;
        }

        return null;
    }

    /// <summary>
    /// Reads exactly one value for a required claim.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <param name="claimType">The claim type.</param>
    /// <param name="value">The single claim value when present.</param>
    /// <returns><see langword="true" /> when exactly one value exists.</returns>
    private static bool TryGetSingleClaim(
        ClaimsPrincipal principal,
        string claimType,
        out string? value)
    {
        var values = principal.FindAll(claimType).ToArray();
        value = values.Length == 1
            ? values[0].Value
            : null;

        return values.Length == 1;
    }

    /// <summary>
    /// Ensures that the converted token retains evidence of a present RS256 signature.
    /// </summary>
    /// <param name="token">The validated token supplied by the framework.</param>
    /// <returns><see langword="true" /> when the token is signed with RS256.</returns>
    private static bool HasValidatedRsaSignature(JwtSecurityToken? token)
    {

        return token is not null &&
            string.Equals(
                token.Header.Alg,
                SecurityAlgorithms.RsaSha256,
                StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(token.RawSignature);
    }

    /// <summary>
    /// Validates that the issued-at claim is a bounded Unix timestamp.
    /// </summary>
    /// <param name="issuedAt">The Unix timestamp claim.</param>
    /// <returns><see langword="true" /> when the timestamp is valid and not in the future.</returns>
    private bool IsIssuedAtValid(string? issuedAt)
    {

        if (!long.TryParse(
                issuedAt,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var secondsSinceEpoch))
            return false;

        try
        {

            return DateTimeOffset.FromUnixTimeSeconds(secondsSinceEpoch) <=
                timeProvider.GetUtcNow().Add(GoogleAuthenticationConstants.ClockSkew);
        }
        catch (ArgumentOutOfRangeException)
        {

            return false;
        }
    }

    /// <summary>
    /// Omits an optional provider display name that does not satisfy MonKado display-name rules.
    /// </summary>
    /// <param name="displayName">The optional provider display name.</param>
    /// <returns>The original valid value, or <see langword="null" />.</returns>
    private static string? NormalizeDisplayName(string? displayName)
    {

        if (string.IsNullOrWhiteSpace(displayName) ||
            !IsWellFormedWithoutControlCharacters(displayName) ||
            displayName.Trim().EnumerateRunes().Count() > DisplayNameValidationExtensions.MaximumLength)
            return null;

        return displayName;
    }

    /// <summary>
    /// Validates UTF-16 input and rejects Unicode control characters.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns><see langword="true" /> when the value is well formed and contains no controls.</returns>
    private static bool IsWellFormedWithoutControlCharacters(string value)
    {
        var remaining = value.AsSpan();

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(
                remaining,
                out var rune,
                out var charactersConsumed);

            if (status is not OperationStatus.Done ||
                Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control)
                return false;

            remaining = remaining[charactersConsumed..];
        }

        return true;
    }

    /// <summary>
    /// Removes provider redemption artifacts and starts the independent external-ticket lifetime.
    /// </summary>
    /// <param name="properties">The protected authentication properties.</param>
    /// <param name="flowBinding">The validated opaque browser-flow binding.</param>
    private void MinimizeProtectedProperties(
        AuthenticationProperties properties,
        string flowBinding)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.Add(GoogleAuthenticationConstants.TransientLifetime);
        var isPersistent = properties.IsPersistent;
        var allowRefresh = properties.AllowRefresh;
        var preservedItems = properties.Items
            .Where(item => item.Key is
                GoogleAuthenticationConstants.ReturnPathProperty or
                GoogleAuthenticationConstants.RememberMeProperty or
                GoogleAuthenticationConstants.FlowIdProperty or
                GoogleAuthenticationConstants.CurrentSessionIdProperty)
            .ToArray();
        properties.Items.Clear();

        foreach (var item in preservedItems)
            properties.Items[item.Key] = item.Value;

        properties.Items[GoogleAuthenticationConstants.FlowBindingProperty] = flowBinding;
        properties.RedirectUri = externalAuthenticationService.BuildBoundPath(
            GoogleAuthenticationConstants.CompletionPath,
            flowBinding);
        properties.IssuedUtc = issuedAt;
        properties.ExpiresUtc = expiresAt;
        properties.IsPersistent = isPersistent;
        properties.AllowRefresh = allowRefresh;
    }

    /// <summary>
    /// Creates a Google identity from the reduced external principal.
    /// </summary>
    /// <param name="principal">The reduced claims principal.</param>
    /// <param name="identity">The resulting identity when all claims are valid.</param>
    /// <returns><see langword="true" /> when the identity can be created.</returns>
    private static bool TryCreateIdentity(
        ClaimsPrincipal principal,
        out GoogleIdentity? identity)
    {
        identity = null;

        if (!TryGetSingleClaim(
                principal,
                JwtRegisteredClaimNames.Sub,
                out var subject) ||
            !TryGetSingleClaim(
                principal,
                EmailClaim,
                out var email) ||
            !TryGetSingleClaim(
                principal,
                EmailVerifiedClaim,
                out var emailVerified) ||
            !bool.TryParse(
                emailVerified,
                out var verified) ||
            !TryGetOptionalSingleClaim(
                principal,
                HostedDomainClaim,
                out var hostedDomain) ||
            !TryGetOptionalSingleClaim(
                principal,
                NameClaim,
                out var displayName))
            return false;

        identity = new GoogleIdentity(
            subject,
            email,
            verified,
            hostedDomain,
            displayName);

        return true;
    }

    /// <summary>
    /// Reads at most one value for an optional claim.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <param name="claimType">The claim type.</param>
    /// <param name="value">The optional claim value.</param>
    /// <returns><see langword="true" /> when no duplicate value exists.</returns>
    private static bool TryGetOptionalSingleClaim(
        ClaimsPrincipal principal,
        string claimType,
        out string? value)
    {
        var values = principal.FindAll(claimType).ToArray();
        value = values.Length == 1
            ? values[0].Value
            : null;

        return values.Length <= 1;
    }

    /// <summary>
    /// Adds an optional claim only when a value is present.
    /// </summary>
    /// <param name="claims">The reduced claim list.</param>
    /// <param name="claimType">The claim type.</param>
    /// <param name="value">The optional claim value.</param>
    private static void AddOptionalClaim(
        List<Claim> claims,
        string claimType,
        string? value)
    {

        if (value is null)
            return;

        claims.Add(new Claim(
            claimType,
            value));
    }
}
