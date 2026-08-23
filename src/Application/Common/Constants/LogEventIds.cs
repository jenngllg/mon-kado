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
    /// Identifies deleted member email change requests.
    /// </summary>
    public const int ExpiredMemberEmailChangeRequestsDeleted = 1060;
    /// <summary>
    /// Identifies a member email change request cleanup failure.
    /// </summary>
    public const int MemberEmailChangeRequestCleanupFailed = 1061;
    /// <summary>
    /// Identifies expected http error.
    /// </summary>

    #endregion

    #region Technical

    public const int ExpectedHttpError = 9000;
    /// <summary>
    /// Identifies dependency unavailable.
    /// </summary>
    public const int DependencyUnavailable = 9001;
    /// <summary>
    /// Identifies unhandled exception.
    /// </summary>
    public const int UnhandledException = 9002;

    #endregion
}
