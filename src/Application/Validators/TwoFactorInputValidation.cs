using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Defines bounded canonical encodings for second-factor inputs.</summary>
public static partial class TwoFactorInputValidation
{
    /// <summary>Checks a canonical, unpadded 256-bit Base64URL proof.</summary>
    /// <param name="flow">The submitted flow.</param>
    /// <returns>Whether the exact encoding is valid, including its unused trailing bits.</returns>
    public static bool IsFlow([NotNullWhen(true)] string? flow)
    {

        return flow is not null && FlowPattern().IsMatch(flow);
    }

    /// <summary>Accepts exactly six ASCII digits without trimming or numeric conversion.</summary>
    /// <param name="code">The submitted authenticator code.</param>
    /// <returns>Whether the code format is valid.</returns>
    public static bool IsCode([NotNullWhen(true)] string? code)
    {

        return code is not null && CodePattern().IsMatch(code);
    }

    /// <summary>Accepts a 128-bit hexadecimal recovery code with optional standard grouping.</summary>
    /// <param name="code">The submitted recovery code.</param>
    /// <returns>Whether the exact format is valid.</returns>
    public static bool IsRecoveryCode([NotNullWhen(true)] string? code)
    {

        return code is not null && RecoveryPattern().IsMatch(code);
    }

    /// <summary>Gets the bounded Base64URL expression.</summary>
    /// <returns>The generated canonical proof expression.</returns>
    [GeneratedRegex("\\A[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]\\z")]
    private static partial Regex FlowPattern();

    /// <summary>Gets the bounded ASCII decimal expression.</summary>
    /// <returns>The generated authenticator expression.</returns>
    [GeneratedRegex("\\A[0-9]{6}\\z")]
    private static partial Regex CodePattern();

    /// <summary>Gets the bounded recovery-code expression.</summary>
    /// <returns>The generated recovery-code expression.</returns>
    [GeneratedRegex("\\A(?:[0-9A-Fa-f]{32}|[0-9A-Fa-f]{8}(?:-[0-9A-Fa-f]{8}){3})\\z")]
    private static partial Regex RecoveryPattern();
}
