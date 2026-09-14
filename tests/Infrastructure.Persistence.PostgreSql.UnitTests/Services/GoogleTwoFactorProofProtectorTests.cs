using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.AspNetCore.DataProtection;

using Moq;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class GoogleTwoFactorProofProtectorTests
{
    private readonly EphemeralDataProtectionProvider _provider = new();
    private readonly GoogleTwoFactorProofProtector _protector;

    public GoogleTwoFactorProofProtectorTests()
    {
        _protector = new GoogleTwoFactorProofProtector(_provider);
    }

    [Fact]
    public void Protect_WhenKeyRingFails_DoesNotExposeProviderException()
    {
        // Arrange
        var dataProtectorMock = new Mock<IDataProtector>(MockBehavior.Strict);
        dataProtectorMock
            .Setup(protector => protector.CreateProtector(It.IsAny<string>()))
            .Returns(dataProtectorMock.Object);
        dataProtectorMock
            .Setup(protector => protector.Protect(It.IsAny<byte[]>()))
            .Throws(new CryptographicException("Private key-ring path must not escape."));
        var protector = new GoogleTwoFactorProofProtector(dataProtectorMock.Object);
        var proof = TestFixture.Create().Create<GoogleTwoFactorProof>();

        // Act
        var exception = Assert.Throws<TwoFactorUnavailableException>(() => protector.Protect(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            proof));

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
    [InlineData(false)]
    [InlineData(true)]
    public void Protect_WhenValidatedProofIsProvided_RoundTripsOnlyWithinItsAccountAndChallenge(bool passwordWasVerified)
    {
        // Arrange
        var context = TestFixture.Create().Create<GoogleAuthenticationContext>();
        var memberId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();

        // Act
        var encrypted = _protector.Protect(
            memberId,
            challengeId,
            new GoogleTwoFactorProof(
                context,
                passwordWasVerified));
        var proof = _protector.Unprotect(
            memberId,
            challengeId,
            encrypted);

        // Assert
        Assert.Equal(
            passwordWasVerified,
            proof.PasswordWasVerified);
        Assert.Equal(
            context.FlowId,
            proof.Context.FlowId);
        Assert.Equal(
            context.Identity.Subject,
            proof.Context.Identity.Subject);
        Assert.Equal(
            context.ExpectedMemberId,
            proof.Context.ExpectedMemberId);
        Assert.Equal(
            context.CurrentSessionId,
            proof.Context.CurrentSessionId);
        Assert.Equal(
            context.IsPersistent,
            proof.Context.IsPersistent);
        Assert.Throws<TwoFactorUnavailableException>(() => _protector.Unprotect(
            Guid.CreateVersion7(),
            challengeId,
            encrypted));
        Assert.Throws<TwoFactorUnavailableException>(() => _protector.Unprotect(
            memberId,
            Guid.CreateVersion7(),
            encrypted));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Context\":{}}")]
    [InlineData("{\"Context\":{\"Identity\":null}}")]
    [InlineData("not json")]
    public void Unprotect_WhenStoredPayloadIsMalformed_ThrowsSanitizedUnavailable(string plaintext)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var encrypted = _provider.CreateProtector(
                "MonKado.TwoFactor.GoogleProof.v1",
                memberId.ToString("N"),
                challengeId.ToString("N"))
            .Protect(plaintext);

        // Act
        var exception = Assert.Throws<TwoFactorUnavailableException>(() => _protector.Unprotect(
            memberId,
            challengeId,
            encrypted));

        // Assert
        Assert.Null(exception.InnerException);
    }
}
