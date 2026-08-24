using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;
/// <summary>
/// Represents log event ids.
/// </summary>

[ExcludeFromCodeCoverage]
public static class LogEventIds
{
    /// <summary>
    /// Identifies authentication email delivery disabled.
    /// </summary>
    #region Account

    public const int AuthenticationEmailDeliveryDisabled = 1000;
    /// <summary>
    /// Identifies authentication email delivery failed.
    /// </summary>
    public const int AuthenticationEmailDeliveryFailed = 1001;
    /// <summary>
    /// Identifies a delivered account confirmation email.
    /// </summary>
    public const int AccountConfirmationEmailSent = 1002;
    /// <summary>
    /// Identifies a delivered member email change confirmation.
    /// </summary>
    public const int MemberEmailChangeConfirmationSent = 1003;
    /// <summary>
    /// Identifies a delivered member email change security notification.
    /// </summary>
    public const int MemberEmailChangeSecurityNotificationSent = 1004;
    /// <summary>
    /// Identifies an authentication email rejected by the provider.
    /// </summary>
    public const int AuthenticationEmailProviderRejectedMessage = 1005;
    /// <summary>
    /// Identifies a delivered member password change security notification.
    /// </summary>
    public const int MemberPasswordChangedSecurityNotificationSent = 1006;
    /// <summary>
    /// Identifies a delivered account password reset email.
    /// </summary>
    public const int AccountPasswordResetEmailSent = 1007;
    /// <summary>
    /// Identifies expired accounts deleted.
    /// </summary>
    public const int ExpiredAccountsDeleted = 1010;
    /// <summary>
    /// Identifies expired account cleanup failed.
    /// </summary>
    public const int ExpiredAccountCleanupFailed = 1011;
    /// <summary>
    /// Identifies expired sessions deleted.
    /// </summary>
    public const int ExpiredSessionsDeleted = 1020;
    /// <summary>
    /// Identifies expired session cleanup failed.
    /// </summary>
    public const int ExpiredSessionCleanupFailed = 1021;
    /// <summary>
    /// Identifies current session retrieval started.
    /// </summary>
    public const int CurrentSessionRetrievalStarted = 1030;
    /// <summary>
    /// Identifies current session retrieved.
    /// </summary>
    public const int CurrentSessionRetrieved = 1031;
    /// <summary>
    /// Identifies current session logout started.
    /// </summary>
    public const int CurrentSessionLogoutStarted = 1032;
    /// <summary>
    /// Identifies current session logout completed.
    /// </summary>
    public const int CurrentSessionLogoutCompleted = 1033;
    /// <summary>
    /// Identifies member profile update started.
    /// </summary>
    public const int MemberProfileUpdateStarted = 1040;
    /// <summary>
    /// Identifies member profile updated.
    /// </summary>
    public const int MemberProfileUpdated = 1041;
    /// <summary>
    /// Identifies a member email change request start.
    /// </summary>
    public const int MemberEmailChangeRequestStarted = 1050;
    /// <summary>
    /// Identifies an accepted member email change request.
    /// </summary>
    public const int MemberEmailChangeRequested = 1051;
    /// <summary>
    /// Identifies a member email change confirmation start.
    /// </summary>
    public const int MemberEmailChangeConfirmationStarted = 1052;
    /// <summary>
    /// Identifies a completed member email change.
    /// </summary>
    public const int MemberEmailChanged = 1053;
    /// <summary>
    /// Identifies the start of a member password change.
    /// </summary>
    public const int MemberPasswordChangeStarted = 1054;
    /// <summary>
    /// Identifies a completed member password change.
    /// </summary>
    public const int MemberPasswordChanged = 1055;
    /// <summary>
    /// Identifies deleted member email change requests.
    /// </summary>
    public const int ExpiredMemberEmailChangeRequestsDeleted = 1060;
    /// <summary>
    /// Identifies a member email change request cleanup failure.
    /// </summary>
    public const int MemberEmailChangeRequestCleanupFailed = 1061;
    /// <summary>
    /// Identifies the start of a password reset email request.
    /// </summary>
    public const int PasswordResetRequestStarted = 1070;
    /// <summary>
    /// Identifies an accepted password reset email request.
    /// </summary>
    public const int PasswordResetRequested = 1071;
    /// <summary>
    /// Identifies the start of an account password reset.
    /// </summary>
    public const int PasswordResetStarted = 1072;
    /// <summary>
    /// Identifies a completed account password reset.
    /// </summary>
    public const int PasswordResetCompleted = 1073;
    /// <summary>
    /// Identifies the start of a Google authentication challenge.
    /// </summary>
    public const int GoogleAuthenticationChallengeStarted = 1080;
    /// <summary>
    /// Identifies a Google identity accepted by the OpenID Connect middleware.
    /// </summary>
    public const int GoogleIdentityValidated = 1081;
    /// <summary>
    /// Identifies a failed Google OpenID Connect protocol exchange.
    /// </summary>
    public const int GoogleAuthenticationProtocolFailed = 1082;
    /// <summary>
    /// Identifies a Google expected-member resolution that failed because PostgreSQL was unavailable.
    /// </summary>
    public const int GoogleExpectedMemberResolutionUnavailable = 1083;
    /// <summary>
    /// Identifies a passwordless member created from Google.
    /// </summary>
    public const int GoogleMemberCreated = 1084;
    /// <summary>
    /// Identifies a member found through an existing Google subject.
    /// </summary>
    public const int GoogleMemberFound = 1085;
    /// <summary>
    /// Identifies Google linked to an existing member.
    /// </summary>
    public const int GoogleAccountLinked = 1086;
    /// <summary>
    /// Identifies a MonKado session created after Google authentication.
    /// </summary>
    public const int GoogleSessionCreated = 1087;
    /// <summary>
    /// Identifies a rejected Google authentication completion.
    /// </summary>
    public const int GoogleAuthenticationCompletionFailed = 1088;
    /// <summary>
    /// Identifies unavailable Google OpenID Connect discovery or signing keys.
    /// </summary>
    public const int GoogleAuthenticationProviderUnavailable = 1089;
    /// <summary>
    /// Identifies deleted processed authentication emails.
    /// </summary>
    public const int ProcessedAuthenticationEmailsDeleted = 1090;
    /// <summary>
    /// Identifies a processed authentication email cleanup failure.
    /// </summary>
    public const int ProcessedAuthenticationEmailCleanupFailed = 1091;
    /// <summary>
    /// Identifies the start of an account registration.
    /// </summary>
    public const int AccountRegistrationStarted = 1100;
    /// <summary>
    /// Identifies an accepted account registration.
    /// </summary>
    public const int AccountRegistrationAccepted = 1101;
    /// <summary>
    /// Identifies the start of an e-mail confirmation.
    /// </summary>
    public const int EmailConfirmationStarted = 1102;
    /// <summary>
    /// Identifies a completed e-mail confirmation.
    /// </summary>
    public const int EmailConfirmationCompleted = 1103;
    /// <summary>
    /// Identifies the start of an e-mail confirmation request.
    /// </summary>
    public const int EmailConfirmationRequestStarted = 1104;
    /// <summary>
    /// Identifies an accepted e-mail confirmation request.
    /// </summary>
    public const int EmailConfirmationRequested = 1105;
    /// <summary>
    /// Identifies the start of a password login.
    /// </summary>
    public const int PasswordLoginStarted = 1106;
    /// <summary>
    /// Identifies a successful password login.
    /// </summary>
    public const int PasswordLoginCompleted = 1107;
    /// <summary>
    /// Identifies the start of a refresh-session rotation.
    /// </summary>
    public const int RefreshSessionStarted = 1108;
    /// <summary>
    /// Identifies a successful refresh-session rotation.
    /// </summary>
    public const int RefreshSessionCompleted = 1109;
    #endregion

    #region Wishlist

    /// <summary>
    /// Identifies the start of a wishlist creation.
    /// </summary>
    public const int WishlistCreationStarted = 2000;

    /// <summary>
    /// Identifies a created wishlist.
    /// </summary>
    public const int WishlistCreated = 2001;

    /// <summary>
    /// Identifies the start of a private wishlist retrieval.
    /// </summary>
    public const int WishlistRetrievalStarted = 2002;

    /// <summary>
    /// Identifies a retrieved private wishlist.
    /// </summary>
    public const int WishlistRetrieved = 2003;

    #endregion

    #region Technical

    /// <summary>
    /// Identifies expected http error.
    /// </summary>
    public const int ExpectedHttpError = 9000;
    /// <summary>
    /// Identifies dependency unavailable.
    /// </summary>
    public const int DependencyUnavailable = 9001;
    /// <summary>
    /// Identifies unhandled exception.
    /// </summary>
    public const int UnhandledException = 9002;
    /// <summary>
    /// Identifies a completed HTTP request logged without query-string values.
    /// </summary>
    public const int HttpRequestCompleted = 9003;

    #endregion
}
