namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates that the second-factor flow does not permit this operation.</summary>
public class TwoFactorOperationConflictException() : Exception("The second-factor flow does not permit this operation.");
