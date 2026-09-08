namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates that the administrative erasure target no longer exists.</summary>
public class AccountErasureTargetNotFoundException() : Exception("The account erasure target was not found.");
