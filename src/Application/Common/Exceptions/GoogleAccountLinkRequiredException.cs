namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Requires the current MonKado password before linking the protected Google identity.</summary>
public class GoogleAccountLinkRequiredException : Exception;
