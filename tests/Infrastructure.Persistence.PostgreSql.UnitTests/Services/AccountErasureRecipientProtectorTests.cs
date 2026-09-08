using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.DataProtection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class AccountErasureRecipientProtectorTests
{
    private readonly AccountErasureRecipientProtector _protector = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Read_WhenOperationMatches_ReturnsProtectedRecipient()
    {
        // Arrange
        var operationId = Guid.CreateVersion7();
        const string Recipient = "erased@example.test";
        var payload = _protector.Protect(
            operationId,
            Recipient);

        // Act
        var recipient = _protector.Read(
            operationId,
            payload);

        // Assert
        Assert.DoesNotContain(
            Recipient,
            payload);
        Assert.Equal(
            Recipient,
            recipient);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_WhenPayloadIsAlteredOrSwapped_ReturnsNull(bool swap)
    {
        // Arrange
        var operationId = Guid.CreateVersion7();
        var payload = _protector.Protect(
            operationId,
            "erased@example.test");

        // Act
        var recipient = _protector.Read(
            swap ? Guid.CreateVersion7() : operationId,
            swap ? payload : "invalid");

        // Assert
        Assert.Null(recipient);
    }
}
