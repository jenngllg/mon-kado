namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL operations for Google external logins.
/// </summary>
public interface IGoogleAccountRepository
{
    /// <summary>
    /// Adds a Google login to the current unit of work.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="subject">The case-sensitive Google subject.</param>
    void AddLogin(
        Guid memberId,
        string subject);

    /// <summary>
    /// Gets the member identifier linked to a Google subject without tracking it.
    /// </summary>
    /// <param name="subject">The case-sensitive Google subject.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The linked member identifier, or <see langword="null" />.</returns>
    Task<Guid?> GetMemberIdBySubjectAsync(
        string subject,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the Google subject already linked to a member without tracking it.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The linked Google subject, or <see langword="null" />.</returns>
    Task<string?> GetSubjectByMemberIdAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}
