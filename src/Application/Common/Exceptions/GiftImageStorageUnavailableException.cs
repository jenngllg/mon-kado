namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents unavailable durable gift-image storage.
/// </summary>
public class GiftImageStorageUnavailableException : DependencyUnavailableException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GiftImageStorageUnavailableException" /> class.
    /// </summary>
    /// <param name="innerException">The underlying storage exception.</param>
    public GiftImageStorageUnavailableException(Exception innerException)
        : base(
            "gift image storage",
            innerException)
    {
    }
}
