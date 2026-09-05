namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents a gift wish that cannot be resolved under a shared wishlist.
/// </summary>
public class SharedWishNotFoundException()
    : Exception("The shared gift wish was not found.");
