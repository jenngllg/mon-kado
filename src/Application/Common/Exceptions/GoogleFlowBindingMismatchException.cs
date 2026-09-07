namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Rejects an unrelated browser flow without deleting another flow's protected cookie.</summary>
public class GoogleFlowBindingMismatchException : GoogleAuthenticationFailedException;
