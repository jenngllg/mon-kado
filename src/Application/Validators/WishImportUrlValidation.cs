namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Validates the syntax of public URL import inputs before network access.</summary>
public static class WishImportUrlValidation
{
    /// <summary>Checks HTTP URL syntax, length, credentials, and standard ports.</summary>
    /// <param name="value">The untrusted URL.</param>
    /// <returns>Whether the URL has supported syntax; DNS still requires validation.</returns>
    public static bool IsValid(string? value)
    {

        return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(
            value,
            UriKind.Absolute,
            out var uri) && uri.UserInfo.Length == 0 && uri.IsDefaultPort && WishTextValidation.IsValidUrl(value);
    }
}
