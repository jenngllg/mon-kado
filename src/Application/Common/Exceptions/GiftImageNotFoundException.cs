namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an unavailable gift image or signed image grant.
/// </summary>
public class GiftImageNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GiftImageNotFoundException" /> class.
    /// </summary>
    public GiftImageNotFoundException() : base("The gift image is unavailable.")
    {
    }
}
