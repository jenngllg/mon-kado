namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents the durable member account deletion request quota being exhausted.</summary>
public class MemberAccountDeletionRateLimitException() : Exception("Too many account deletion requests. Try again later.");
