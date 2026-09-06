using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

/// <summary>Validates bounded import options at application startup.</summary>
public class UrlImportOptionsValidator : IValidateOptions<UrlImportOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(
        string? name,
        UrlImportOptions options)
    {

        return options.TimeoutSeconds is >= 1 and <= 20 && options.MaximumHtmlBytes is >= 1 and <= 2 * 1024 * 1024 && options.MaximumRedirects is >= 0 and <= 3 && options.PermitLimit is >= 1 and <= 10 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("URL import limits must be positive and must not exceed their supported safety caps.");
    }
}
