namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents recognized image content that cannot be processed safely.
/// </summary>
public class GiftImageInvalidException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GiftImageInvalidException" /> class.
    /// </summary>
    /// <param name="message">The safe diagnostic message.</param>
    public GiftImageInvalidException(string message) : base(message)
    {
    }
}
