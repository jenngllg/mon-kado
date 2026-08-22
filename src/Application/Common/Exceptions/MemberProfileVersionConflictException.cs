namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an optimistic concurrency conflict while updating a member profile.
/// </summary>
public class MemberProfileVersionConflictException()
    : Exception("The member profile has changed since it was retrieved.");
