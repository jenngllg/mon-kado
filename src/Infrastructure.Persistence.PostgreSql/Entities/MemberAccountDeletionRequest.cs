namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Stores the account security state authorized by a deletion request.</summary>
public class MemberAccountDeletionRequest
{
    private MemberAccountDeletionRequest()
    {
    }

    /// <summary>Creates a confirmation request for the current account security state.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="email">The confirmed recipient address.</param>
    /// <param name="securityStamp">The current security stamp.</param>
    /// <param name="createdAt">The current UTC time.</param>
    /// <param name="lifetime">The configured confirmation lifetime.</param>
    public MemberAccountDeletionRequest(
        Guid memberId,
        string email,
        string securityStamp,
        DateTime createdAt,
        TimeSpan lifetime)
    {
        Id = Guid.CreateVersion7();
        MemberId = memberId;
        Email = email;
        SecurityStamp = securityStamp;
        CreatedAt = createdAt;
        ExpiresAt = createdAt.Add(lifetime);
    }

    /// <summary>Gets the immutable request identifier.</summary>
    public Guid Id
    {
        get; private set;
    }
    /// <summary>Gets the member identifier.</summary>
    public Guid MemberId
    {
        get; private set;
    }
    /// <summary>Gets the confirmed recipient address snapshot.</summary>
    public string Email { get; private set; } = string.Empty;
    /// <summary>Gets the security stamp snapshot.</summary>
    public string SecurityStamp { get; private set; } = string.Empty;
    /// <summary>Gets the request creation time in UTC.</summary>
    public DateTime CreatedAt
    {
        get; private set;
    }
    /// <summary>Gets the absolute confirmation deadline in UTC.</summary>
    public DateTime ExpiresAt
    {
        get; private set;
    }

    /// <summary>Checks whether the request still authorizes the current account state.</summary>
    /// <param name="email">The current confirmed address.</param>
    /// <param name="securityStamp">The current security stamp.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>Whether the request is current and unexpired.</returns>
    public bool IsValid(
        string? email,
        string? securityStamp,
        DateTime now)
    {

        return now < ExpiresAt && string.Equals(
            Email,
            email,
            StringComparison.OrdinalIgnoreCase) && string.Equals(
            SecurityStamp,
            securityStamp,
            StringComparison.Ordinal);
    }
}
