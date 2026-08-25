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
}
