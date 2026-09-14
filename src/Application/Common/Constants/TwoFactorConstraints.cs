using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Defines the administrator second-factor security contract.</summary>
[ExcludeFromCodeCoverage]
public static class TwoFactorConstraints
{
    /// <summary>The authenticator account issuer.</summary>
    public const string Issuer = "MonKado";

    /// <summary>The entropy of the authenticator shared secret in bytes.</summary>
    public const int SecretByteLength = 20;

    /// <summary>The number of decimal digits in authenticator codes.</summary>
    public const int CodeLength = 6;

    /// <summary>The length of an authenticator time step in seconds.</summary>
    public const int PeriodSeconds = 30;

    /// <summary>The absolute challenge lifetime in minutes.</summary>
    public const int ChallengeLifetimeMinutes = 5;

    /// <summary>The cleanup grace period after challenge expiration in hours.</summary>
    public const int CleanupGraceHours = 1;

    /// <summary>The number of recovery codes in a newly generated set.</summary>
    public const int RecoveryCodeCount = 10;

    /// <summary>The entropy of each recovery code in bytes.</summary>
    public const int RecoveryCodeByteLength = 16;

    /// <summary>The hexadecimal length of an unformatted recovery code.</summary>
    public const int RecoveryCodeLength = RecoveryCodeByteLength * 2;

    /// <summary>The entropy of a challenge proof in bytes.</summary>
    public const int FlowByteLength = 32;

    /// <summary>The canonical unpadded Base64URL challenge proof length.</summary>
    public const int FlowLength = 43;

    /// <summary>The maximum number of consecutive incorrect codes before lockout.</summary>
    public const int MaximumFailedAttempts = 5;

    /// <summary>The duration of a second-factor lockout in minutes.</summary>
    public const int LockoutMinutes = 15;

    /// <summary>The number of second-factor verifications permitted per account per minute.</summary>
    public const int VerificationsPerMinute = 10;
}
