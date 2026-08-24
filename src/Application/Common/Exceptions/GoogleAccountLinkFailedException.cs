namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an explicit Google account link that could not prove the local account.
/// </summary>
public class GoogleAccountLinkFailedException()
    : Exception("The Google account could not be linked.");
