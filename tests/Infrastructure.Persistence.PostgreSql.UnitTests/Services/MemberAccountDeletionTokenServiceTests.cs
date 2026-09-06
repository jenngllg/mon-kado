using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.DataProtection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class MemberAccountDeletionTokenServiceTests
{
    private readonly EphemeralDataProtectionProvider _provider = new();
    private readonly MemberAccountDeletionTokenService _tokenService;
    public MemberAccountDeletionTokenServiceTests()
    {
        _tokenService = new MemberAccountDeletionTokenService(_provider);
    }

    [Fact]
    public void Read_WhenCreatedForMember_ReturnsRequestIdentifier()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var requestId = Guid.CreateVersion7();
        var token = _tokenService.Create(
            memberId,
            requestId);

        // Act
        var actual = _tokenService.Read(
            memberId,
            token);

        // Assert
        Assert.Equal(
            requestId,
            actual);
        Assert.DoesNotContain(
            requestId.ToString("N"),
            token);
    }

    [Fact]
    public void Read_WhenMemberDiffers_RejectsToken()
    {
        // Arrange
        var token = _tokenService.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        // Act
        var result = _tokenService.Read(
            Guid.CreateVersion7(),
            token);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("!!!")]
    public void Read_WhenTokenIsMalformed_RejectsToken(string token)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();

        // Act
        var result = _tokenService.Read(
            memberId,
            token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Read_WhenProtectedPayloadIsNotRequestIdentifier_RejectsToken()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var protector = _provider.CreateProtector(
            "MonKado.MemberAccountDeletion.v1",
            memberId.ToString("N"));
        var token = protector.Protect("invalid-payload");

        // Act
        var result = _tokenService.Read(
            memberId,
            token);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Read_WhenTokenIsAltered_RejectsToken()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var token = _tokenService.Create(
            memberId,
            Guid.CreateVersion7());
        var altered = token[..20] + (token[20] == 'A' ? 'B' : 'A') + token[21..];

        // Act
        var result = _tokenService.Read(
            memberId,
            altered);

        // Assert
        Assert.Null(result);
    }
}
