namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an email address already assigned to another member.
/// </summary>
public class MemberEmailAlreadyUsedException()
    : Exception("The requested email address is already assigned to another member.");
