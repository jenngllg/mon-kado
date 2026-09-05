using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>
/// Defines structured application log messages.
/// </summary>
public static partial class ApplicationLogMessages
{
    /// <summary>Logs the start of an anonymous wishlist report creation.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistReportCreationStarted,
        Level = LogLevel.Debug,
        Message = "Creating report {ReportId} through share link {ShareLinkId}.")]
    public static partial void WishlistReportCreationStarted(
        ILogger logger,
        Guid shareLinkId,
        Guid reportId);

    /// <summary>Logs a created anonymous wishlist report.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="reportId">The report identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistReportCreated,
        Level = LogLevel.Information,
        Message = "Report {ReportId} created for wishlist {WishlistId}.")]
    public static partial void WishlistReportCreated(
        ILogger logger,
        Guid wishlistId,
        Guid reportId);

    /// <summary>Logs the start of a shared-wishlist participant join.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistParticipantJoinStarted,
        Level = LogLevel.Debug,
        Message = "Joining shared wishlist through link {ShareLinkId}.")]
    public static partial void WishlistParticipantJoinStarted(
        ILogger logger,
        Guid shareLinkId);

    /// <summary>Logs a joined shared-wishlist participant.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistParticipantJoined,
        Level = LogLevel.Information,
        Message = "Participant {ParticipantId} joined wishlist {WishlistId}.")]
    public static partial void WishlistParticipantJoined(
        ILogger logger,
        Guid wishlistId,
        Guid participantId);

    /// <summary>Logs the start of current-participant retrieval.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistParticipantRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving the current participant through link {ShareLinkId}.")]
    public static partial void WishlistParticipantRetrievalStarted(
        ILogger logger,
        Guid shareLinkId);

    /// <summary>Logs a retrieved current participant.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistParticipantRetrieved,
        Level = LogLevel.Information,
        Message = "Participant {ParticipantId} retrieved from wishlist {WishlistId}.")]
    public static partial void WishlistParticipantRetrieved(
        ILogger logger,
        Guid wishlistId,
        Guid participantId);

    /// <summary>
    /// Logs the start of share-link creation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkCreationStarted,
        Level = LogLevel.Debug,
        Message = "Creating share link {ShareLinkId} for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkCreationStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid shareLinkId);

    /// <summary>
    /// Logs a created share link.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkCreated,
        Level = LogLevel.Information,
        Message = "Share link {ShareLinkId} created for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkCreated(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid shareLinkId);

    /// <summary>
    /// Logs the start of owner share-link retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving share link for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkRetrievalStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs an owner-retrieved share link.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkRetrieved,
        Level = LogLevel.Information,
        Message = "Share link {ShareLinkId} retrieved for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkRetrieved(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid shareLinkId);

    /// <summary>
    /// Logs the start of share-link rotation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkRotationStarted,
        Level = LogLevel.Debug,
        Message = "Rotating share link for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkRotationStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs a rotated share link.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkRotated,
        Level = LogLevel.Information,
        Message = "Share link {ShareLinkId} rotated for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkRotated(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid shareLinkId);

    /// <summary>
    /// Logs the start of share-link revocation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkDeletionStarted,
        Level = LogLevel.Debug,
        Message = "Revoking share link for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkDeletionStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs a revoked share link.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistShareLinkDeleted,
        Level = LogLevel.Information,
        Message = "Share link revoked for wishlist {WishlistId} and member {MemberId}.")]
    public static partial void WishlistShareLinkDeleted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of public wishlist retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.SharedWishlistRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving public wishlist through share link {ShareLinkId}.")]
    public static partial void SharedWishlistRetrievalStarted(
        ILogger logger,
        Guid shareLinkId);

    /// <summary>
    /// Logs a publicly retrieved wishlist.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.SharedWishlistRetrieved,
        Level = LogLevel.Information,
        Message = "Wishlist {WishlistId} retrieved through share link {ShareLinkId}.")]
    public static partial void SharedWishlistRetrieved(
        ILogger logger,
        Guid shareLinkId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of detailed public gift-wish retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.SharedWishRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving wish {WishId} through share link {ShareLinkId}.")]
    public static partial void SharedWishRetrievalStarted(
        ILogger logger,
        Guid shareLinkId,
        Guid wishId);

    /// <summary>
    /// Logs a detailed publicly retrieved gift wish.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.SharedWishRetrieved,
        Level = LogLevel.Information,
        Message = "Wish {WishId} from wishlist {WishlistId} retrieved through share link {ShareLinkId}.")]
    public static partial void SharedWishRetrieved(
        ILogger logger,
        Guid shareLinkId,
        Guid wishlistId,
        Guid wishId);

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

    /// <summary>
    /// Logs the start of an account registration.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.AccountRegistrationStarted,
        Level = LogLevel.Debug,
        Message = "Registering an account.")]
    public static partial void AccountRegistrationStarted(ILogger logger);

    /// <summary>
    /// Logs an accepted account registration.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.AccountRegistrationAccepted,
        Level = LogLevel.Information,
        Message = "Account registration accepted.")]
    public static partial void AccountRegistrationAccepted(ILogger logger);

    /// <summary>
    /// Logs the start of an e-mail confirmation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.EmailConfirmationStarted,
        Level = LogLevel.Debug,
        Message = "Confirming an account e-mail address.")]
    public static partial void EmailConfirmationStarted(ILogger logger);

    /// <summary>
    /// Logs a completed e-mail confirmation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.EmailConfirmationCompleted,
        Level = LogLevel.Information,
        Message = "Account e-mail address confirmed.")]
    public static partial void EmailConfirmationCompleted(ILogger logger);

    /// <summary>
    /// Logs the start of an e-mail confirmation request.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.EmailConfirmationRequestStarted,
        Level = LogLevel.Debug,
        Message = "Requesting an account e-mail confirmation.")]
    public static partial void EmailConfirmationRequestStarted(ILogger logger);

    /// <summary>
    /// Logs an accepted e-mail confirmation request.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.EmailConfirmationRequested,
        Level = LogLevel.Information,
        Message = "Account e-mail confirmation request accepted.")]
    public static partial void EmailConfirmationRequested(ILogger logger);

    /// <summary>
    /// Logs the start of a password login.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.PasswordLoginStarted,
        Level = LogLevel.Debug,
        Message = "Authenticating an account with a password.")]
    public static partial void PasswordLoginStarted(ILogger logger);

    /// <summary>
    /// Logs a successful password login.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.PasswordLoginCompleted,
        Level = LogLevel.Information,
        Message = "Password authentication completed.")]
    public static partial void PasswordLoginCompleted(ILogger logger);

    /// <summary>
    /// Logs the start of a refresh-session rotation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.RefreshSessionStarted,
        Level = LogLevel.Debug,
        Message = "Rotating an authentication refresh session.")]
    public static partial void RefreshSessionStarted(ILogger logger);

    /// <summary>
    /// Logs a successful refresh-session rotation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        EventId = LogEventIds.RefreshSessionCompleted,
        Level = LogLevel.Information,
        Message = "Authentication refresh session rotated.")]
    public static partial void RefreshSessionCompleted(ILogger logger);

    /// <summary>
    /// Logs the start of a wishlist creation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The generated wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistCreationStarted,
        Level = LogLevel.Debug,
        Message = "Creating wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishlistCreationStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs a created wishlist.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistCreated,
        Level = LogLevel.Information,
        Message = "Wishlist {WishlistId} created for member {MemberId}.")]
    public static partial void WishlistCreated(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of a private wishlist retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishlistRetrievalStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs a retrieved private wishlist.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistRetrieved,
        Level = LogLevel.Information,
        Message = "Wishlist {WishlistId} retrieved for member {MemberId}.")]
    public static partial void WishlistRetrieved(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of an owned wishlist collection retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistCollectionRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving wishlists for member {MemberId}.")]
    public static partial void WishlistCollectionRetrievalStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs a retrieved owned wishlist collection.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistCollectionRetrieved,
        Level = LogLevel.Information,
        Message = "Wishlists retrieved for member {MemberId}.")]
    public static partial void WishlistCollectionRetrieved(
        ILogger logger,
        Guid memberId);

    /// <summary>
    /// Logs the start of a private wishlist update.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistUpdateStarted,
        Level = LogLevel.Debug,
        Message = "Updating wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishlistUpdateStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs an updated private wishlist.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistUpdated,
        Level = LogLevel.Information,
        Message = "Wishlist {WishlistId} updated for member {MemberId}.")]
    public static partial void WishlistUpdated(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of a private wishlist deletion.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistDeletionStarted,
        Level = LogLevel.Debug,
        Message = "Deleting wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishlistDeletionStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs a deleted private wishlist.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishlistDeleted,
        Level = LogLevel.Information,
        Message = "Wishlist {WishlistId} deleted for member {MemberId}.")]
    public static partial void WishlistDeleted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of a manual gift wish creation.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The generated wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishCreationStarted,
        Level = LogLevel.Debug,
        Message = "Creating wish {WishId} in wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishCreationStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Logs a created gift wish.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishCreated,
        Level = LogLevel.Information,
        Message = "Wish {WishId} created in wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishCreated(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Logs the start of a private gift wish retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving wish {WishId} from wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishRetrievalStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Logs a retrieved private gift wish.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishRetrieved,
        Level = LogLevel.Information,
        Message = "Wish {WishId} retrieved from wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishRetrieved(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Logs the start of a gift wish collection retrieval.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishCollectionRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving wishes from wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishCollectionRetrievalStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs a retrieved gift wish collection.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishCollectionRetrieved,
        Level = LogLevel.Information,
        Message = "Wishes retrieved from wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishCollectionRetrieved(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of a gift wish collection reorder.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishReorderStarted,
        Level = LogLevel.Debug,
        Message = "Reordering wishes in wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishReorderStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs a reordered gift wish collection.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishReordered,
        Level = LogLevel.Information,
        Message = "Wishes reordered in wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishReordered(
        ILogger logger,
        Guid memberId,
        Guid wishlistId);

    /// <summary>
    /// Logs the start of a gift wish update.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishUpdateStarted,
        Level = LogLevel.Debug,
        Message = "Updating wish {WishId} in wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishUpdateStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Logs an updated gift wish.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishUpdated,
        Level = LogLevel.Information,
        Message = "Wish {WishId} updated in wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishUpdated(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Logs the start of a gift wish deletion.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishDeletionStarted,
        Level = LogLevel.Debug,
        Message = "Deleting wish {WishId} from wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishDeletionStarted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>
    /// Logs a deleted gift wish.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.WishDeleted,
        Level = LogLevel.Information,
        Message = "Wish {WishId} deleted from wishlist {WishlistId} for member {MemberId}.")]
    public static partial void WishDeleted(
        ILogger logger,
        Guid memberId,
        Guid wishlistId,
        Guid wishId);

    /// <summary>Logs the start of a gift-reservation mutation.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationMutationStarted,
        Level = LogLevel.Debug,
        Message = "Mutating a reservation for wish {WishId} through share link {ShareLinkId}.")]
    public static partial void GiftReservationMutationStarted(
        ILogger logger,
        Guid shareLinkId,
        Guid wishId);

    /// <summary>Logs a created or replaced gift reservation.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="reservationId">The reservation identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationMutated,
        Level = LogLevel.Information,
        Message = "Reservation {ReservationId} for wish {WishId} in wishlist {WishlistId} was created or replaced.")]
    public static partial void GiftReservationMutated(
        ILogger logger,
        Guid wishlistId,
        Guid wishId,
        Guid reservationId);

    /// <summary>Logs the start of a current gift-reservation retrieval.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving the current reservation for wish {WishId} through share link {ShareLinkId}.")]
    public static partial void GiftReservationRetrievalStarted(
        ILogger logger,
        Guid shareLinkId,
        Guid wishId);

    /// <summary>Logs a retrieved current gift reservation.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="reservationId">The reservation identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationRetrieved,
        Level = LogLevel.Information,
        Message = "Reservation {ReservationId} for wish {WishId} in wishlist {WishlistId} was retrieved.")]
    public static partial void GiftReservationRetrieved(
        ILogger logger,
        Guid wishlistId,
        Guid wishId,
        Guid reservationId);

    /// <summary>Logs the start of a gift-reservation cancellation.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationCancellationStarted,
        Level = LogLevel.Debug,
        Message = "Cancelling a reservation for wish {WishId} through share link {ShareLinkId}.")]
    public static partial void GiftReservationCancellationStarted(
        ILogger logger,
        Guid shareLinkId,
        Guid wishId);

    /// <summary>Logs a cancelled gift reservation.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationCancelled,
        Level = LogLevel.Information,
        Message = "The current participant's reservation for wish {WishId} in wishlist {WishlistId} was cancelled.")]
    public static partial void GiftReservationCancelled(
        ILogger logger,
        Guid wishlistId,
        Guid wishId);

    /// <summary>Logs the start of a member reservation history retrieval.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationHistoryRetrievalStarted,
        Level = LogLevel.Debug,
        Message = "Retrieving reservation history for member {MemberId}.")]
    public static partial void GiftReservationHistoryRetrievalStarted(
        ILogger logger,
        Guid memberId);

    /// <summary>Logs a retrieved member reservation history page.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="totalCount">The total matching history entry count.</param>
    [LoggerMessage(
        EventId = LogEventIds.GiftReservationHistoryRetrieved,
        Level = LogLevel.Information,
        Message = "Reservation history containing {TotalCount} matching entries was retrieved for member {MemberId}.")]
    public static partial void GiftReservationHistoryRetrieved(
        ILogger logger,
        Guid memberId,
        int totalCount);
}
