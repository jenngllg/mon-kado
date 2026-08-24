using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>
/// Defines structured application log messages.
/// </summary>
public static partial class ApplicationLogMessages
{
    /// <summary>
    /// Logs the start of a current session retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving the current session for member {MemberId}.")]
    public static partial void CurrentSessionRetrievalStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a successful current session retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionRetrieved,
        Level = LogLevel.Information,
        Message = "Current session retrieved for member {MemberId}.")]
    public static partial void CurrentSessionRetrieved(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs the start of a current session logout.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionLogoutStarted,
        Level = LogLevel.Debug,
        Message = "Logging out the current browser session.")]
    public static partial void CurrentSessionLogoutStarted(ILogger logger);

    /// <summary>
    /// Logs a completed current session logout.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.CurrentSessionLogoutCompleted,
        Level = LogLevel.Information,
        Message = "Current browser session logout completed.")]
    public static partial void CurrentSessionLogoutCompleted(ILogger logger);

    /// <summary>
    /// Logs the start of a member profile update.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberProfileUpdateStarted,
        Level = LogLevel.Debug,
        Message = "Updating the profile for member {MemberId}.")]
    public static partial void MemberProfileUpdateStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a successful member profile update.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberProfileUpdated,
        Level = LogLevel.Information,
        Message = "Profile updated for member {MemberId}.")]
    public static partial void MemberProfileUpdated(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs the start of a member email change request.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberEmailChangeRequestStarted,
        Level = LogLevel.Debug,
        Message = "Requesting an email change for member {MemberId}.")]
    public static partial void MemberEmailChangeRequestStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs an accepted member email change request.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberEmailChangeRequested,
        Level = LogLevel.Information,
        Message = "Email change requested for member {MemberId}.")]
    public static partial void MemberEmailChangeRequested(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs the start of a member email change confirmation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="requestId">The email change request identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberEmailChangeConfirmationStarted,
        Level = LogLevel.Debug,
        Message = "Confirming member email change request {RequestId}.")]
    public static partial void MemberEmailChangeConfirmationStarted(
        ILogger logger,
        Guid requestId);

    /// <summary>
    /// Logs a completed member email change.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="requestId">The email change request identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberEmailChanged,
        Level = LogLevel.Information,
        Message = "Member email change request {RequestId} completed.")]
    public static partial void MemberEmailChanged(
        ILogger logger,
        Guid requestId);

    /// <summary>
    /// Logs the start of a member password change.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberPasswordChangeStarted,
        Level = LogLevel.Debug,
        Message = "Changing the password for member {MemberId}.")]
    public static partial void MemberPasswordChangeStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a completed member password change.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.MemberPasswordChanged,
        Level = LogLevel.Information,
        Message = "Password changed for member {MemberId}.")]
    public static partial void MemberPasswordChanged(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs the start of a password reset email request.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.PasswordResetRequestStarted,
        Level = LogLevel.Debug,
        Message = "Requesting a password reset email.")]
    public static partial void PasswordResetRequestStarted(ILogger logger);

    /// <summary>
    /// Logs an accepted password reset email request.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.PasswordResetRequested,
        Level = LogLevel.Information,
        Message = "Password reset email request accepted.")]
    public static partial void PasswordResetRequested(ILogger logger);

    /// <summary>
    /// Logs the start of an account password reset.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The account identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.PasswordResetStarted,
        Level = LogLevel.Debug,
        Message = "Resetting the password for member {MemberId}.")]
    public static partial void PasswordResetStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a completed account password reset.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The account identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.PasswordResetCompleted,
        Level = LogLevel.Information,
        Message = "Password reset completed for member {MemberId}.")]
    public static partial void PasswordResetCompleted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a passwordless member created from Google.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleMemberCreated,
        Level = LogLevel.Information,
        Message = "Passwordless member {MemberId} created from Google authentication.")]
    public static partial void GoogleMemberCreated(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a member found through an existing Google subject.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleMemberFound,
        Level = LogLevel.Information,
        Message = "Member {MemberId} found through an existing Google login.")]
    public static partial void GoogleMemberFound(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs Google linked to an existing member.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleAccountLinked,
        Level = LogLevel.Information,
        Message = "Google login linked to member {MemberId}.")]
    public static partial void GoogleAccountLinked(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a MonKado session created after Google authentication.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GoogleSessionCreated,
        Level = LogLevel.Information,
        Message = "Google authentication session created for member {MemberId}.")]
    public static partial void GoogleSessionCreated(
        ILogger logger,
        Guid memberId);
}
