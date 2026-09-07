using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.PersonalDataExports.Options;

/// <summary>Rejects missing, invalid or filesystem-root archive locations before canonical path resolution.</summary>
public class PersonalDataExportStorageOptionsValidator : IValidateOptions<PersonalDataExportStorageOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(
        string? name,
        PersonalDataExportStorageOptions options)
    {
        var path = options.StoragePath;

        if (string.IsNullOrWhiteSpace(path))
            return ValidateOptionsResult.Fail("PersonalDataExports:StoragePath must identify a private directory.");
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

            if (fullPath == Path.GetPathRoot(fullPath))
                return ValidateOptionsResult.Fail("PersonalDataExports:StoragePath must not be a filesystem root.");
        }
        catch (ArgumentException)
        {

            return ValidateOptionsResult.Fail("PersonalDataExports:StoragePath is invalid.");
        }

        return ValidateOptionsResult.Success;
    }
}
