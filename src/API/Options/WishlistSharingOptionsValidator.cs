using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.Options;

/// <summary>
/// Validates wishlist-sharing configuration.
/// </summary>
/// <param name="environment">The current host environment.</param>
public class WishlistSharingOptionsValidator(IWebHostEnvironment environment)
    : IValidateOptions<WishlistSharingOptions>
{
    /// <summary>Validates an options instance.</summary>
    /// <param name="name">The options name.</param>
    /// <param name="options">The options.</param>
    /// <returns>The validation result.</returns>
    public ValidateOptionsResult Validate(
        string? name,
        WishlistSharingOptions options)
    {
        _ = name;
        var origin = options.FrontendOrigin;

        if (string.IsNullOrWhiteSpace(origin))
            return ValidateOptionsResult.Fail("WishlistSharing:FrontendOrigin is required.");

        try
        {
            Extensions.WebSecurityExtensions.ValidateOrigin(
                origin,
                environment);
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
