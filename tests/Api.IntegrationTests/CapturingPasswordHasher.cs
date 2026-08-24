using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Captures password hash operations while preserving Identity password behavior.
/// </summary>
public class CapturingPasswordHasher : IPasswordHasher<MonKadoUser>
{
    private readonly PasswordHasher<MonKadoUser> _innerHasher = new();
    private int _hashCount;

    /// <summary>
    /// Gets the number of password hash operations.
    /// </summary>
    public int HashCount => Volatile.Read(ref _hashCount);

    /// <inheritdoc />
    public string HashPassword(
        MonKadoUser user,
        string password)
    {
        _ = Interlocked.Increment(ref _hashCount);

        return _innerHasher.HashPassword(
            user,
            password);
    }

    /// <inheritdoc />
    public PasswordVerificationResult VerifyHashedPassword(
        MonKadoUser user,
        string hashedPassword,
        string providedPassword)
    {

        return _innerHasher.VerifyHashedPassword(
            user,
            hashedPassword,
            providedPassword);
    }
}
