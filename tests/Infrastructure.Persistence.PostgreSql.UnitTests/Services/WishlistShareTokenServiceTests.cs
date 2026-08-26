using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistShareTokenServiceTests
{
    private readonly WishlistShareTokenService _tokenService = new(
        new EphemeralDataProtectionProvider());

    [Fact]
    public void Create_WhenCalled_ReturnsProtected256BitSecretAndMatchingHash()
    {
        // Arrange

        // Act
        var token = _tokenService.Create();

        // Assert
        Assert.Equal(
            32,
            WebEncoders.Base64UrlDecode(token.Secret).Length);
        Assert.Equal(
            32,
            token.SecretHash.Length);
        Assert.NotEqual(
            token.Secret,
            token.ProtectedSecret);
        Assert.Equal(
            token.Secret,
            _tokenService.Unprotect(token.ProtectedSecret));
        Assert.True(_tokenService.Verify(
            token.Secret,
            token.SecretHash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("__________________________________________8")]
    public void Verify_WhenSecretFormatIsInvalid_ReturnsFalse(string secret)
    {
        // Arrange
        var token = _tokenService.Create();

        // Act
        var verified = _tokenService.Verify(
            secret,
            token.SecretHash);

        // Assert
        Assert.False(verified);
    }

    [Fact]
    public void Verify_WhenHashLengthIsInvalid_ReturnsFalse()
    {
        // Arrange
        var token = _tokenService.Create();

        // Act
        var verified = _tokenService.Verify(
            token.Secret,
            [1]);

        // Assert
        Assert.False(verified);
    }

    [Fact]
    public void Verify_WhenSecretDoesNotMatch_ReturnsFalse()
    {
        // Arrange
        var first = _tokenService.Create();
        var second = _tokenService.Create();

        // Act
        var verified = _tokenService.Verify(
            first.Secret,
            second.SecretHash);

        // Assert
        Assert.False(verified);
    }
}
