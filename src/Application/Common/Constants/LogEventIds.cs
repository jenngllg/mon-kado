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
