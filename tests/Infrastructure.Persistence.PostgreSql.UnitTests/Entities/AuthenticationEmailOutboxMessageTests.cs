using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class AuthenticationEmailOutboxMessageTests
{
    private readonly DateTime _now = new(
        2026,
        8,
        22,
        20,
        0,
        0,
        DateTimeKind.Utc);

    [Theory]
    [InlineData(true, AuthenticationEmailKind.EmailChangeConfirmation, "new@example.fr")]
    [InlineData(false, AuthenticationEmailKind.EmailChangeSecurityNotification, "old@example.fr")]
    public void CreateEmailChange_WhenCalled_CreatesRequestSpecificMessage(
        bool confirmation,
        AuthenticationEmailKind expectedKind,
        string recipientEmail)
    {
        // Arrange
        var requestId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        // Act
        var message = confirmation
            ? AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
                requestId,
                userId,
                recipientEmail,
                _now)
            : AuthenticationEmailOutboxMessage.CreateEmailChangeSecurityNotification(
                requestId,
                userId,
                recipientEmail,
                _now);

        // Assert
        Assert.Equal(
            7,
            message.Id.Version);
        Assert.Equal(
            requestId,
            message.MemberEmailChangeRequestId);
        Assert.Equal(
            userId,
            message.UserId);
        Assert.Equal(
            recipientEmail,
            message.RecipientEmail);
        Assert.Equal(
            expectedKind,
            message.Kind);
        Assert.Equal(
            _now,
            message.CreatedAt);
        Assert.Equal(
            _now,
            message.AvailableAt);
    }

    [Fact]
    public void CreatePasswordChangedSecurityNotification_WhenCalled_CreatesSnapshotMessage()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var message = AuthenticationEmailOutboxMessage
            .CreatePasswordChangedSecurityNotification(
                userId,
                "member@example.fr",
                _now);

        // Assert
        Assert.Equal(
            7,
            message.Id.Version);
        Assert.Equal(
            userId,
            message.UserId);
        Assert.Equal(
            "member@example.fr",
            message.RecipientEmail);
        Assert.Equal(
            AuthenticationEmailKind.PasswordChangedSecurityNotification,
            message.Kind);
        Assert.Equal(
            _now,
            message.CreatedAt);
        Assert.Equal(
            _now,
            message.AvailableAt);
        Assert.Null(message.MemberEmailChangeRequestId);
        Assert.Null(message.SecurityStampSnapshot);
    }

    [Fact]
    public void CreatePasswordReset_WhenCalled_CreatesSecuritySnapshotMessage()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var message = AuthenticationEmailOutboxMessage.CreatePasswordReset(
            userId,
            "member@example.fr",
            "security-stamp",
            _now);

        // Assert
        Assert.Equal(
            7,
            message.Id.Version);
        Assert.Equal(
            userId,
            message.UserId);
        Assert.Equal(
            "member@example.fr",
            message.RecipientEmail);
        Assert.Equal(
            "security-stamp",
            message.SecurityStampSnapshot);
        Assert.Equal(
            AuthenticationEmailKind.PasswordReset,
            message.Kind);
        Assert.Equal(
            _now,
            message.CreatedAt);
        Assert.Equal(
            _now,
            message.AvailableAt);
        Assert.Null(message.MemberEmailChangeRequestId);
    }
}
