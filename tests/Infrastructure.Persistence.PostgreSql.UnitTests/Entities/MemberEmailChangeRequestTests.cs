using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class MemberEmailChangeRequestTests
{
    private readonly DateTime _now = new(
        2026,
        8,
        22,
        20,
        0,
        0,
        DateTimeKind.Utc);

    [Fact]
    public void Create_WhenValuesAreValid_CreatesActiveVersionSevenRequest()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var request = MemberEmailChangeRequest.Create(
            userId,
            "old@example.fr",
            "new@example.fr",
            "NEW@EXAMPLE.FR",
            _now,
            _now.AddHours(24));

        // Assert
        Assert.Equal(
            7,
            request.Id.Version);
        Assert.Equal(
            userId,
            request.UserId);
        Assert.Equal(
            "old@example.fr",
            request.CurrentEmail);
        Assert.Equal(
            "new@example.fr",
            request.NewEmail);
        Assert.Equal(
            "NEW@EXAMPLE.FR",
            request.NormalizedNewEmail);
        Assert.Equal(
            _now,
            request.CreatedAt);
        Assert.Equal(
            _now.AddHours(24),
            request.ExpiresAt);
        Assert.Null(request.ConfirmedAt);
        Assert.Null(request.RevokedAt);
        Assert.True(request.IsActive(_now));
        Assert.False(request.IsActive(_now.AddHours(24)));
    }

    [Fact]
    public void Revoke_WhenCalledTwice_PreservesFirstRevocationDateAndDisablesRequest()
    {
        // Arrange
        var request = CreateRequest();

        // Act
        request.Revoke(_now.AddMinutes(1));
        request.Revoke(_now.AddMinutes(2));

        // Assert
        Assert.Equal(
            _now.AddMinutes(1),
            request.RevokedAt);
        Assert.False(request.IsActive(_now.AddMinutes(1)));
    }

    [Fact]
    public void Confirm_WhenCalled_SetsConfirmationDateAndDisablesRequest()
    {
        // Arrange
        var request = CreateRequest();

        // Act
        request.Confirm(_now.AddMinutes(1));

        // Assert
        Assert.Equal(
            _now.AddMinutes(1),
            request.ConfirmedAt);
        Assert.False(request.IsActive(_now.AddMinutes(1)));
    }

    private MemberEmailChangeRequest CreateRequest()
    {
        return MemberEmailChangeRequest.Create(
            Guid.CreateVersion7(),
            "old@example.fr",
            "new@example.fr",
            "NEW@EXAMPLE.FR",
            _now,
            _now.AddHours(24));
    }
}
