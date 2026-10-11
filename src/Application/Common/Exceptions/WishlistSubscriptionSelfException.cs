namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents a member cannot subscribe to their own wishlist.</summary>
public class WishlistSubscriptionSelfException() : Exception("A member cannot subscribe to their own wishlist.");
