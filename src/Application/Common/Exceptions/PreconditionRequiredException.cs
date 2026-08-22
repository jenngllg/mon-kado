namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents a missing request precondition.
/// </summary>
public class PreconditionRequiredException()
    : Exception("A required request precondition is missing.");
