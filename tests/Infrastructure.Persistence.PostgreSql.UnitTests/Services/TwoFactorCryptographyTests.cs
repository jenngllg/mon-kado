using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

using Moq;

using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class TwoFactorCryptographyTests
{
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
    private readonly EphemeralDataProtectionProvider _protection = new();
    private readonly TwoFactorCryptography _cryptography;

    public TwoFactorCryptographyTests()
    {
        _cryptography = new TwoFactorCryptography(
            _protection,
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(59)));
    }

    [Fact]
    public void ProtectSecret_WhenKeyRingFails_ReturnsSanitizedUnavailable()
    {
        // Arrange
        var dataProtectorMock = new Mock<IDataProtector>(MockBehavior.Strict);
        dataProtectorMock
            .Setup(protector => protector.CreateProtector(It.IsAny<string>()))
            .Returns(dataProtectorMock.Object);
        dataProtectorMock
            .Setup(protector => protector.Protect(It.IsAny<byte[]>()))
            .Throws(new CryptographicException("Private key-ring path must not escape."));
        var cryptography = new TwoFactorCryptography(
            dataProtectorMock.Object,
            TimeProvider.System);

        // Act
        var exception = Assert.Throws<TwoFactorUnavailableException>(() => cryptography.ProtectSecret(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            RfcSecret));

        // Assert
        Assert.Null(exception.InnerException);
        dataProtectorMock.Verify(
            candidate => candidate.CreateProtector(It.IsAny<string>()),
            Times.Exactly(3));
        dataProtectorMock.Verify(
            candidate => candidate.Protect(It.IsAny<byte[]>()),
            Times.Once);
        dataProtectorMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void VerifyCode_WhenRfcSha1VectorMatches_ReturnsExactTimeStep(
        long timestamp,
        string code)
    {
        // Arrange
        var cryptography = new TwoFactorCryptography(
            _protection,
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(timestamp)));

        // Act
        var result = cryptography.VerifyCode(
            RfcSecret,
            code,
            null);

        // Assert
        Assert.Equal(
            timestamp / 30,
            result);
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(29L, true)]
    [InlineData(30L, true)]
    [InlineData(59L, true)]
    [InlineData(60L, true)]
    [InlineData(89L, true)]
    [InlineData(90L, false)]
    public void VerifyCode_WhenClockMoves_UsesOneIntervalTolerance(
        long timestamp,
        bool accepted)
    {
        // Arrange
        var cryptography = new TwoFactorCryptography(
            _protection,
            new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(timestamp)));

        // Act
        var result = cryptography.VerifyCode(
            RfcSecret,
            "287082",
            null);

        // Assert
        Assert.Equal(
            accepted,
            result.HasValue);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    public void VerifyCode_WhenStepWasAlreadyConsumed_RejectsReplay(long lastStep)
    {
        // Arrange
        // Act
        var result = _cryptography.VerifyCode(
            RfcSecret,
            "287082",
            lastStep);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void VerifyCode_WhenPreviousStepWasConsumed_AcceptsNewStep()
    {
        // Arrange
        // Act
        var result = _cryptography.VerifyCode(
            RfcSecret,
            "287082",
            0);

        // Assert
        Assert.Equal(
            1L,
            result);
    }

    [Fact]
    public void VerifyCode_WhenCodeDoesNotMatch_RejectsCode()
    {
        // Arrange
        // Act
        var result = _cryptography.VerifyCode(
            RfcSecret,
            "000000",
            null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ProtectSecret_WhenOwnerAndVersionMatch_RoundTripsWithoutPlaintext()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var credentialId = Guid.CreateVersion7();
        var secret = _cryptography.CreateSecret();

        // Act
        var encrypted = _cryptography.ProtectSecret(
            memberId,
            credentialId,
            secret);
        var decrypted = _cryptography.UnprotectSecret(
            memberId,
            credentialId,
            encrypted);

        // Assert
        Assert.Equal(
            secret,
            decrypted);
        Assert.Matches(
            "^[A-Z2-7]{32}$",
            secret);
        Assert.DoesNotContain(
            secret,
            encrypted,
            StringComparison.Ordinal);
        Assert.NotEqual(
            secret,
            _cryptography.CreateSecret());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnprotectSecret_WhenPurposeDiffers_FailsClosed(bool changeOwner)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var credentialId = Guid.CreateVersion7();
        var encrypted = _cryptography.ProtectSecret(
            memberId,
            credentialId,
            RfcSecret);

        // Act
        var exception = Assert.Throws<TwoFactorUnavailableException>(() => _cryptography.UnprotectSecret(
            changeOwner ? Guid.CreateVersion7() : memberId,
            changeOwner ? credentialId : Guid.CreateVersion7(),
            encrypted));

        // Assert
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            encrypted,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnprotectSecret_WhenKeyRingIsLost_FailsClosed()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var credentialId = Guid.CreateVersion7();
        var encrypted = _cryptography.ProtectSecret(
            memberId,
            credentialId,
            RfcSecret);
        var other = new TwoFactorCryptography(
            new EphemeralDataProtectionProvider(),
            TimeProvider.System);

        // Act
        var exception = Assert.Throws<TwoFactorUnavailableException>(() => other.UnprotectSecret(
            memberId,
            credentialId,
            encrypted));

        // Assert
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void UnprotectSecret_WhenPayloadIsAltered_FailsClosed()
    {
        // Arrange
        // Act
        var exception = Assert.Throws<TwoFactorUnavailableException>(() => _cryptography.UnprotectSecret(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "invalid"));

        // Assert
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void CreateSetupUri_WhenLabelContainsReservedCharacters_EncodesLabel()
    {
        // Arrange
        // Act
        var result = _cryptography.CreateSetupUri(
            RfcSecret,
            "member?issuer=other#fragment");

        // Assert
        Assert.Equal(
            $"otpauth://totp/MonKado%3Amember%3Fissuer%3Dother%23fragment?secret={RfcSecret}&issuer=MonKado&algorithm=SHA1&digits=6&period=30",
            result);
    }

    [Fact]
    public void CreateRecoveryCodes_WhenRequested_GeneratesTenIndependent128BitCodes()
    {
        // Arrange
        // Act
        var codes = _cryptography.CreateRecoveryCodes();

        // Assert
        Assert.Equal(
            10,
            codes.Count);
        Assert.Equal(
            codes.Count,
            codes.Distinct().Count());
        Assert.All(
            codes,
            code => Assert.Matches(
                "^[A-F0-9]{8}(-[A-F0-9]{8}){3}$",
                code));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData(" 01234567-89abcdef-01234567-89abcdef ")]
    public void HashRecoveryCode_WhenPresentationDiffers_UsesCanonicalHash(string code)
    {
        // Arrange
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes("0123456789ABCDEF0123456789ABCDEF"));

        // Act
        var result = _cryptography.HashRecoveryCode(code);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void CreateFlow_WhenRequested_ReturnsCanonicalProofAndOnlyItsHash()
    {
        // Arrange
        // Act
        var first = _cryptography.CreateFlow();
        var second = _cryptography.CreateFlow();

        // Assert
        Assert.Matches(
            "^[A-Za-z0-9_-]{43}$",
            first.Value);
        Assert.Equal(
            32,
            WebEncoders.Base64UrlDecode(first.Value).Length);
        Assert.Equal(
            first.Value,
            WebEncoders.Base64UrlEncode(WebEncoders.Base64UrlDecode(first.Value)));
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(first.Value)),
            first.Hash);
        Assert.NotEqual(
            first.Value,
            second.Value);
        Assert.NotEqual(
            first.Hash,
            second.Hash);
    }
}
