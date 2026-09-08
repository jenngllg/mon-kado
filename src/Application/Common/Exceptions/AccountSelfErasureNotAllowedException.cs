namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Rejects using administrative erasure for the acting administrator's own account.</summary>
public class AccountSelfErasureNotAllowedException() : Exception("Administrative self-erasure is not allowed.");
