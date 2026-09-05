namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an attempt to reduce a gift quantity below its already reserved quantity.
/// </summary>
public class WishQuantityBelowReservedException : Exception;
