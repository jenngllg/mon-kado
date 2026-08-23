namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an invalid, expired, revoked, or reused member email change confirmation.
/// </summary>
public class MemberEmailChangeInvalidException()
    : Exception("The member email change confirmation is invalid or expired.");
