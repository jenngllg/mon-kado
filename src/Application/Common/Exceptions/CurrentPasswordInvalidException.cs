namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents a failed current-password verification for a sensitive member operation.
/// </summary>
public class CurrentPasswordInvalidException()
    : Exception("The current password is invalid.");
