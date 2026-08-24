using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Constants;
using JennGllg.Fr.MonKado.Back.Api.Models;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Creates and consumes the Data Protection state used by the short Google authentication flow.
/// </summary>
public class GoogleExternalAuthenticationService(
    TimeProvider timeProvider,
    IGoogleReturnPathService returnPathService) : IGoogleExternalAuthenticationService
{
    private const int FlowBindingByteLength = 32;
    private const int FlowBindingEncodedLength = 43;
    private const string EmailClaim = "email";
    private const string EmailVerifiedClaim = "email_verified";
    private const string HostedDomainClaim = "hd";
    private const string NameClaim = "name";

    /// <inheritdoc />
    public AuthenticationProperties CreateChallengeProperties(
        string returnPath,
        bool rememberMe,
        Guid? currentSessionId)
    {
        var now = timeProvider.GetUtcNow();
        var properties = new AuthenticationProperties
        {
            AllowRefresh = false,
            ExpiresUtc = now.Add(
                GoogleAuthenticationConstants.TransientLifetime),
            IsPersistent = false,
            RedirectUri = GoogleAuthenticationConstants.CompletionPath
        };
        properties.Items[GoogleAuthenticationConstants.ReturnPathProperty] = returnPath;
        properties.Items[GoogleAuthenticationConstants.RememberMeProperty] = rememberMe
            ? "1"
            : "0";
        properties.Items[GoogleAuthenticationConstants.FlowIdProperty] = Guid.CreateVersion7(
            now)
            .ToString("D");

        properties.Items[GoogleAuthenticationConstants.CurrentSessionIdProperty] =
            currentSessionId.HasValue
                ? currentSessionId.Value.ToString("D")
                : GoogleAuthenticationConstants.NoCurrentSessionValue;

        return properties;
    }

    /// <inheritdoc />
    public string CreateFlowBinding()
    {

        return WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(FlowBindingByteLength));
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async Task<GoogleExternalAuthenticationTicket?> AuthenticateAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await context.AuthenticateAsync(GoogleAuthenticationSchemes.ExternalCookie);
        cancellationToken.ThrowIfCancellationRequested();

        if (!result.Succeeded ||
            result.Principal is null ||
            result.Properties is null ||
            !TryReadProperties(
                result.Properties,
                out var returnPath,
                out var rememberMe,
                out var flowId,
                out var flowBinding,
                out var expectedMemberId,
                out var currentSessionId) ||
            !TryGetSingleClaim(
                result.Principal,
                JwtRegisteredClaimNames.Sub,
                out var subject) ||
            !TryGetSingleClaim(
                result.Principal,
                EmailClaim,
                out var email) ||
            !TryGetSingleClaim(
                result.Principal,
                EmailVerifiedClaim,
                out var emailVerified) ||
            !bool.TryParse(
                emailVerified,
                out var verified) ||
            !TryGetOptionalSingleClaim(
                result.Principal,
                HostedDomainClaim,
                out var hostedDomain) ||
            !TryGetOptionalSingleClaim(
                result.Principal,
                NameClaim,
                out var displayName))
            return null;

        var identity = new GoogleIdentity(
            subject,
            email,
            verified,
            hostedDomain,
            displayName);

        return new GoogleExternalAuthenticationTicket(
            new GoogleAuthenticationContext(
                identity,
                rememberMe,
                returnPath,
                flowId,
                expectedMemberId,
                currentSessionId),
            flowBinding);
    }

    /// <inheritdoc />
    public bool TryGetFlowBinding(
        AuthenticationProperties properties,
        out string flowBinding)
    {
        flowBinding = string.Empty;

        if (!properties.Items.TryGetValue(
                GoogleAuthenticationConstants.FlowBindingProperty,
                out var storedFlowBinding) ||
            storedFlowBinding is null ||
            !TryDecodeFlowBinding(
                storedFlowBinding,
                out _))
            return false;

        flowBinding = storedFlowBinding;

        return true;
    }

    /// <inheritdoc />
    public bool MatchesFlowBinding(
        string protectedFlowBinding,
        string? browserFlowBinding)
    {

        if (!TryDecodeFlowBinding(
                protectedFlowBinding,
                out var protectedBytes) ||
            !TryDecodeFlowBinding(
                browserFlowBinding,
                out var browserBytes))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            protectedBytes,
            browserBytes);
    }

    /// <inheritdoc />
    public string BuildBoundPath(
        string path,
        string flowBinding)
    {
        var separator = path.Contains(
            '?',
            StringComparison.Ordinal)
            ? '&'
            : '?';

        return $"{path}{separator}{GoogleAuthenticationConstants.FlowBindingParameter}={flowBinding}";
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public Task DeleteAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return context.SignOutAsync(GoogleAuthenticationSchemes.ExternalCookie);
    }

    /// <summary>
    /// Reads the protected navigation and prior-session values from authentication properties.
    /// </summary>
    /// <param name="properties">The protected authentication properties.</param>
    /// <param name="returnPath">The validated frontend return path.</param>
    /// <param name="rememberMe">Whether the requested MonKado session is persistent.</param>
    /// <param name="flowId">The one-time Google authentication flow identifier.</param>
    /// <param name="flowBinding">The opaque browser-flow binding.</param>
    /// <param name="expectedMemberId">The optional member resolved at callback time.</param>
    /// <param name="currentSessionId">The optional previously proven session identifier.</param>
    /// <returns><see langword="true" /> when every protected value is valid.</returns>
    private bool TryReadProperties(
        AuthenticationProperties properties,
        out string returnPath,
        out bool rememberMe,
        out Guid flowId,
        out string flowBinding,
        out Guid? expectedMemberId,
        out Guid? currentSessionId)
    {
        returnPath = string.Empty;
        rememberMe = false;
        flowId = Guid.Empty;
        flowBinding = string.Empty;
        expectedMemberId = null;
        currentSessionId = null;

        if (!properties.Items.TryGetValue(
                GoogleAuthenticationConstants.ReturnPathProperty,
                out var storedReturnPath) ||
            !properties.Items.TryGetValue(
                GoogleAuthenticationConstants.RememberMeProperty,
                out var storedRememberMe) ||
            storedRememberMe is not "0" and not "1" ||
            !properties.Items.TryGetValue(
                GoogleAuthenticationConstants.FlowIdProperty,
                out var storedFlowId) ||
            !Guid.TryParseExact(
                storedFlowId,
                "D",
                out flowId) ||
            flowId == Guid.Empty ||
            !TryGetFlowBinding(
                properties,
                out flowBinding))
            return false;

        try
        {
            returnPath = returnPathService.Resolve(storedReturnPath);
        }
        catch (RequestValidationException)
        {

            return false;
        }

        if (!properties.Items.TryGetValue(
            GoogleAuthenticationConstants.ExpectedMemberIdProperty,
            out var storedExpectedMemberId))
            return false;

        if (!string.Equals(
            storedExpectedMemberId,
            GoogleAuthenticationConstants.NoExpectedMemberValue,
            StringComparison.Ordinal))
        {

            if (!Guid.TryParseExact(
                    storedExpectedMemberId,
                    "D",
                    out var parsedExpectedMemberId))
                return false;

            expectedMemberId = parsedExpectedMemberId;
        }

        if (!properties.Items.TryGetValue(
            GoogleAuthenticationConstants.CurrentSessionIdProperty,
            out var storedSessionId))
            return false;

        rememberMe = storedRememberMe == "1";

        if (string.Equals(
            storedSessionId,
            GoogleAuthenticationConstants.NoCurrentSessionValue,
            StringComparison.Ordinal))
            return true;

        if (!Guid.TryParseExact(
                storedSessionId,
                "D",
                out var parsedSessionId))
            return false;

        currentSessionId = parsedSessionId;

        return true;
    }

    /// <summary>
    /// Decodes an opaque browser-flow binding with its exact expected length.
    /// </summary>
    /// <param name="flowBinding">The encoded flow binding.</param>
    /// <param name="bytes">The decoded binding bytes when valid.</param>
    /// <returns><see langword="true" /> when the value is a valid 256-bit binding.</returns>
    private static bool TryDecodeFlowBinding(
        string? flowBinding,
        out byte[] bytes)
    {
        bytes = [];

        if (flowBinding?.Length != FlowBindingEncodedLength)
            return false;

        try
        {
            bytes = WebEncoders.Base64UrlDecode(flowBinding);

            return bytes.Length == FlowBindingByteLength;
        }
        catch (FormatException)
        {

            return false;
        }
    }

    /// <summary>
    /// Reads exactly one required claim.
    /// </summary>
    /// <param name="principal">The protected external principal.</param>
    /// <param name="claimType">The claim type.</param>
    /// <param name="value">The single claim value.</param>
    /// <returns><see langword="true" /> when exactly one value is present.</returns>
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
    /// Reads at most one optional claim.
    /// </summary>
    /// <param name="principal">The protected external principal.</param>
    /// <param name="claimType">The claim type.</param>
    /// <param name="value">The optional claim value.</param>
    /// <returns><see langword="true" /> when zero or one value is present.</returns>
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
}
