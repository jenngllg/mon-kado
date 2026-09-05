using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.Options;

/// <summary>
/// Validates durable local gift-image storage settings.
/// </summary>
public class GiftImageStorageOptionsValidator : IValidateOptions<GiftImageStorageOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(
        string? name,
        GiftImageStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StoragePath))
        {
            return ValidateOptionsResult.Fail(
                "'GiftImages:StoragePath' is required.");
        }

        return ValidateOptionsResult.Success;
    }
}
