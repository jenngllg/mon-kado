using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class RefreshTokenServiceTests
{
    private readonly RefreshTokenService _service = new();

    [Fact]
    public void Create_WhenSessionIdIsProvided_ReturnsVerifiableToken()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var result = _service.Create(sessionId);
        var parsed = _service.TryGetSessionId(
            result.Value,
            out var parsedSessionId);
        var verified = _service.Verify(
            result.Value,
            result.Hash);

        // Assert
        Assert.True(parsed);
        Assert.Equal(
            sessionId,
            parsedSessionId);
        Assert.True(verified);
        Assert.Equal(
            SHA256.HashSizeInBytes,
            result.Hash.Length);
        Assert.DoesNotContain(
            Convert.ToBase64String(result.Hash),
            result.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WhenCalledTwice_ReturnsDifferentSecretsForSameSession()
    {
        // Arrange
        var sessionId = Guid.NewGuid();

        // Act
        var first = _service.Create(sessionId);
        var second = _service.Create(sessionId);

        // Assert
        Assert.NotEqual(
            first.Value,
            second.Value);
        Assert.False(_service.Verify(
            first.Value,
            second.Hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000000000000000000000000000.invalid!")]
    [InlineData("00000000000000000000000000000000.AQ")]
    [InlineData("00000000000000000000000000000000.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.extra")]
    public void TryGetSessionId_WhenTokenIsInvalid_ReturnsFalse(string value)
    {
        // Arrange
        // Act
        var result = _service.TryGetSessionId(
            value,
            out var sessionId);

        // Assert
        Assert.False(result);
        Assert.Equal(
            Guid.Empty,
            sessionId);
    }

    [Fact]
    public void Verify_WhenExpectedHashHasUnexpectedLength_ReturnsFalse()
    {
        // Arrange
        var token = _service.Create(Guid.NewGuid());

        // Act
        var result = _service.Verify(
            token.Value,
            []);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Hash_WhenValueIsProvided_IsDeterministic()
    {
        // Arrange
        const string value = "refresh-token";

        // Act
        var first = RefreshTokenService.Hash(value);
        var second = RefreshTokenService.Hash(value);

        // Assert
        Assert.Equal(
            first,
            second);
    }
}
