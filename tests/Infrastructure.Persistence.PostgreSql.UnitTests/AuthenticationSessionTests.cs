using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class AuthenticationSessionTests
{
    [Fact]
    public void Create_WhenValuesAreProvided_InitializesRenewableSession()
    {
        // Arrange
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        byte[] hash = [1, 2, 3];
        var now = new DateTime(
            2026,
            8,
            21,
            10,
            0,
            0,
            DateTimeKind.Utc);
        var expiresAt = now.AddHours(8);

        // Act
        var result = AuthenticationSession.Create(
            id,
            userId,
            hash,
            false,
            now,
            expiresAt);

        // Assert
        Assert.Equal(
            id,
            result.Id);
        Assert.Equal(
            userId,
            result.UserId);
        Assert.Same(
            hash,
            result.RefreshTokenHash);
        Assert.False(result.IsPersistent);
        Assert.Equal(
            now,
            result.CreatedAt);
        Assert.Equal(
            now,
            result.RenewedAt);
        Assert.Equal(
            expiresAt,
            result.ExpiresAt);
        Assert.Null(result.RevokedAt);
    }

    [Fact]
    public void Rotate_WhenValuesAreProvided_ReplacesRenewalState()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var session = AuthenticationSession.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [1],
            false,
            now,
            now.AddHours(8));
        byte[] hash = [2];
        var renewedAt = now.AddHours(1);
        var expiresAt = renewedAt.AddHours(8);

        // Act
        session.Rotate(
            hash,
            renewedAt,
            expiresAt);

        // Assert
        Assert.Same(
            hash,
            session.RefreshTokenHash);
        Assert.Equal(
            renewedAt,
            session.RenewedAt);
        Assert.Equal(
            expiresAt,
            session.ExpiresAt);
    }

    [Fact]
    public void Revoke_WhenCalledTwice_PreservesFirstRevocationDate()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var session = AuthenticationSession.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [1],
            false,
            now,
            now.AddHours(8));
        var first = now.AddMinutes(1);

        // Act
        session.Revoke(first);
        session.Revoke(now.AddMinutes(2));

        // Assert
        Assert.Equal(
            first,
            session.RevokedAt);
    }
}
