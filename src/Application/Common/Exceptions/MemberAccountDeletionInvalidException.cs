namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents an invalid, superseded or expired account deletion confirmation.</summary>
public class MemberAccountDeletionInvalidException() : Exception("The account deletion link is invalid or expired.");
