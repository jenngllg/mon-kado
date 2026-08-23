using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class AuthenticationEmailTokenEncodingTests
{
    [Theory]
    [InlineData("token")]
    [InlineData("token+/=with-unicode-🎁")]
    public void Encode_WhenDecoded_RoundTripsWithoutUnsafeBase64Characters(string token)
    {
        // Arrange
        // Act
        var encoded = AuthenticationEmailTokenEncoding.Encode(token);
        var success = AuthenticationEmailTokenEncoding.TryDecode(
            encoded,
            out var decoded);

        // Assert
        Assert.True(success);
        Assert.Equal(
            token,
            decoded);
        Assert.DoesNotContain(
            "+",
            encoded,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/",
            encoded,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "=",
            encoded,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("***")]
    [InlineData("_w")]
    public void TryDecode_WhenTokenIsInvalid_ReturnsFalse(string token)
    {
        // Arrange
        // Act
        var success = AuthenticationEmailTokenEncoding.TryDecode(
            token,
            out var decoded);

        // Assert
        Assert.False(success);
        Assert.Equal(
            string.Empty,
            decoded);
    }
}
