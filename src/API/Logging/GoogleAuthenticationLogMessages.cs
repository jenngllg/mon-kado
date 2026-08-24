using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Api.Logging;

/// <summary>
/// Defines structured logs emitted at the Google OpenID Connect boundary.
/// </summary>
public static partial class GoogleAuthenticationLogMessages
{
    /// <summary>
    /// Logs that a Google authentication challenge is starting.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleAuthenticationChallengeStarted,
        Level = LogLevel.Information,
        Message = "Google authentication challenge started.")]
    public static partial void ChallengeStarted(ILogger logger);

    /// <summary>
    /// Logs that Google identity claims passed protocol validation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleIdentityValidated,
        Level = LogLevel.Debug,
        Message = "Google identity passed OpenID Connect validation.")]
    public static partial void IdentityValidated(ILogger logger);

    /// <summary>
    /// Logs a classified Google protocol failure without provider details.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="failureType">The technical exception type.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleAuthenticationProtocolFailed,
        Level = LogLevel.Error,
        Message = "Google authentication protocol failed. Failure type: {FailureType}.")]
    public static partial void ProtocolFailed(
        ILogger logger,
        string failureType);

    /// <summary>
    /// Logs unavailable persistence while the callback resolves its expected member snapshot.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The classified dependency exception.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleExpectedMemberResolutionUnavailable,
        Level = LogLevel.Error,
        Message = "Google identity snapshot resolution is temporarily unavailable.")]
    public static partial void ExpectedMemberResolutionUnavailable(
        ILogger logger,
        Exception exception);

    /// <summary>
    /// Logs a classified completion failure without identity or token values.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="classification">The non-sensitive failure classification.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleAuthenticationCompletionFailed,
        Level = LogLevel.Error,
        Message = "Google authentication completion failed. Classification: {Classification}.")]
    public static partial void CompletionFailed(
        ILogger logger,
        string classification);

    /// <summary>
    /// Logs unavailable Google discovery, signing keys or token transport without provider response values.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="failureType">The non-sensitive provider failure type.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleAuthenticationProviderUnavailable,
        Level = LogLevel.Error,
        Message = "Google authentication provider is temporarily unavailable. Failure type: {FailureType}.")]
    public static partial void ProviderUnavailable(
        ILogger logger,
        string failureType);
}
