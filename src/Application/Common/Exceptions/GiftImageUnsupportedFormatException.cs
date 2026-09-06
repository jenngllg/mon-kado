namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents unsupported gift-image content.
/// </summary>
public class GiftImageUnsupportedFormatException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GiftImageUnsupportedFormatException" /> class.
    /// </summary>
    public GiftImageUnsupportedFormatException()
        : base("The supplied gift image format is not supported.")
    {
    }
}
