namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an ambiguous Google account link caused by concurrent account state.
/// </summary>
public class GoogleAccountLinkConflictException()
    : Exception("The Google account link conflicts with the current account state.");
