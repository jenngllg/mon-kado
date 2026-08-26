using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class GuestSessionTokenServiceTests
{
    private readonly GuestSessionTokenService _tokenService = new();

    [Fact]
    public void Create_WhenSessionIdIsProvided_Returns256BitSecretAndHash()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7();

        // Act
        var token = _tokenService.Create(sessionId);

        // Assert
        Assert.Equal(
            sessionId,
            token.SessionId);
        Assert.StartsWith(
            $"{sessionId:N}.",
            token.Secret,
            StringComparison.Ordinal);
        Assert.Equal(
            32,
            token.SecretHash.Length);
        Assert.True(_tokenService.TryParse(
            token.Secret,
            out var parsedSessionId,
            out var parsedHash));
        Assert.Equal(
            sessionId,
            parsedSessionId);
        Assert.True(_tokenService.Verify(
            parsedHash,
            token.SecretHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("invalid.AAAA")]
    [InlineData("0198e75d828070008000000000000001.invalid!")]
    [InlineData("0198e75d828070008000000000000001.AQID")]
    public void TryParse_WhenTokenIsInvalid_ReturnsFalse(string token)
    {
        // Arrange
        // Act
        var result = _tokenService.TryParse(
            token,
            out var sessionId,
            out var hash);

        // Assert
        Assert.False(result);
        Assert.True(sessionId == Guid.Empty || token.StartsWith(
            sessionId.ToString("N"),
            StringComparison.Ordinal));
        Assert.Empty(hash);
    }

    [Fact]
    public void Verify_WhenHashesDiffer_ReturnsFalse()
    {
        // Arrange
        var expected = new byte[32];
        var differentLength = new byte[31];
        var differentValue = Enumerable.Repeat(
            (byte)1,
            32)
            .ToArray();

        // Act
        var lengthResult = _tokenService.Verify(
            expected,
            differentLength);
        var valueResult = _tokenService.Verify(
            expected,
            differentValue);

        // Assert
        Assert.False(lengthResult);
        Assert.False(valueResult);
    }
}
