using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates canonical 256-bit Base64URL Google browser proofs.</summary>
public static partial class GoogleFlowValidation
{
    /// <summary>Checks the exact encoding, including its unused trailing bits.</summary>
    /// <param name="flow">The submitted or protected binding.</param>
    /// <returns>Whether the value is a canonical 32-byte Base64URL binding.</returns>
    public static bool IsCanonical([NotNullWhen(true)] string? flow)
    {

        return flow is not null && CanonicalPattern()
            .IsMatch(flow);
    }

    /// <summary>Gets the bounded canonical binding pattern.</summary>
    /// <returns>The generated binding expression.</returns>
    [GeneratedRegex("\\A[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]\\z")]
    private static partial Regex CanonicalPattern();
}
