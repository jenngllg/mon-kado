namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>The report changed since it was read.</summary>
public class WishlistReportVersionConflictException() : Exception("The report changed since it was read.");
