using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class GuestSessionTests
{
    [Fact]
    public void Constructor_WhenValuesAreProvided_StoresTokenAndExpiration()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        byte[] hash = [1, 2, 3];
        var expiresAt = new DateTime(
            2027,
            2,
            22,
            12,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        var session = new GuestSession(
            id,
            hash,
            expiresAt);

        // Assert
        Assert.Equal(
            id,
            session.Id);
        Assert.NotSame(
            hash,
            session.SecretHash);
        Assert.Equal(
            hash,
            session.SecretHash);
        Assert.Equal(
            expiresAt,
            session.ExpiresAt);
        Assert.Equal(
            default,
            session.CreatedAt);
        Assert.Null(session.UpdatedAt);
    }
}
