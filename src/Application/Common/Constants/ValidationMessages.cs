using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;
/// <summary>
/// Represents validation messages.
/// </summary>

[ExcludeFromCodeCoverage]
public static class ValidationMessages
{
    /// <summary>
    /// Identifies mandatory property.
    /// </summary>
    public const string MandatoryProperty = "The property {PropertyName} is mandatory.";
    /// <summary>
    /// Identifies invalid email address.
    /// </summary>
    public const string InvalidEmailAddress = "The email address is invalid.";
    /// <summary>
    /// Identifies email address too long.
    /// </summary>
    public const string EmailAddressTooLong = "The email address must not exceed 254 characters.";
    /// <summary>
    /// Identifies invalid email confirmation link.
    /// </summary>
    public const string InvalidEmailConfirmationLink = "The email confirmation link is invalid.";
    /// <summary>
    /// Identifies an invalid member email change confirmation link.
    /// </summary>
    public const string InvalidEmailChangeConfirmationLink = "The email change confirmation link is invalid.";
    /// <summary>
    /// Identifies an invalid password reset link.
    /// </summary>
    public const string InvalidPasswordResetLink = "The password reset link is invalid.";
    /// <summary>
    /// Identifies a password below the minimum supported length.
    /// </summary>
    public const string PasswordTooShort = "The password must contain at least 12 characters.";
    /// <summary>
    /// Identifies a password above the maximum supported length.
    /// </summary>
    public const string PasswordTooLong = "The password must not exceed 128 characters.";
    /// <summary>
    /// Identifies a new password that is identical to the current password.
    /// </summary>
    public const string NewPasswordMustDiffer = "The new password must differ from the current password.";

    /// <summary>
    /// Identifies an invalid wishlist name.
    /// </summary>
    public const string InvalidWishlistName = "The wishlist name must be a single line of at most 100 characters.";
    /// <summary>
    /// Identifies an invalid wishlist occasion.
    /// </summary>
    public const string InvalidWishlistOccasion = "The wishlist occasion is invalid.";
    /// <summary>
    /// Identifies an invalid wishlist message.
    /// </summary>
    public const string InvalidWishlistMessage = "The wishlist message must not exceed 500 characters or contain unsupported control characters.";
    /// <summary>
    /// Identifies a wishlist event date earlier than the current date.
    /// </summary>
    public const string WishlistEventDateMustBeTodayOrLater = "The event date must be today or later.";

    /// <summary>
    /// Identifies an invalid gift wish name.
    /// </summary>
    public const string InvalidWishName = "The wish name must be a single line of at most 100 characters.";

    /// <summary>
    /// Identifies an invalid gift wish note.
    /// </summary>
    public const string InvalidWishNote = "The wish note must not exceed 500 characters or contain unsupported control characters.";

    /// <summary>
    /// Identifies an invalid gift wish URL.
    /// </summary>
    public const string InvalidWishUrl = "The wish URL must be an absolute HTTP or HTTPS URL of at most 2048 characters without embedded credentials.";

    /// <summary>
    /// Identifies an invalid gift wish price.
    /// </summary>
    public const string InvalidWishPrice = "The wish price must be greater than zero and contain at most two decimal places.";

    /// <summary>
    /// Identifies an invalid gift quantity message.
    /// </summary>
    public const string InvalidGiftQuantity = "The gift quantity must be between 1 and 100.";

    /// <summary>
    /// Identifies an invalid one-based page number.
    /// </summary>
    public const string InvalidPage = "The page must be greater than or equal to 1.";

    /// <summary>
    /// Identifies an invalid page size.
    /// </summary>
    public const string InvalidPageSize = "The page size must be between 1 and 100.";

    /// <summary>
    /// Identifies an invalid reservation history status.
    /// </summary>
    public const string InvalidGiftReservationHistoryStatus = "The reservation history status must be active, cancelled or unavailable.";

    /// <summary>
    /// Identifies an invalid wishlist report reason.
    /// </summary>
    public const string InvalidWishlistReportReason = "The wishlist report reason is invalid.";

    /// <summary>
    /// Identifies invalid wishlist report details.
    /// </summary>
    public const string InvalidWishlistReportDetails = "The wishlist report details must not exceed 1000 characters or contain unsupported control characters.";
}
