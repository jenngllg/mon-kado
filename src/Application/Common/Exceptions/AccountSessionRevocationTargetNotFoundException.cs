namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates that the account selected for session revocation no longer exists.</summary>
public class AccountSessionRevocationTargetNotFoundException() : Exception("The account was not found.");
