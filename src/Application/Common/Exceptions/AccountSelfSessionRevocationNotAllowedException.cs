namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Rejects administrative revocation of the caller's own sessions.</summary>
public class AccountSelfSessionRevocationNotAllowedException() : Exception("Administrative self-session revocation is not allowed.");
