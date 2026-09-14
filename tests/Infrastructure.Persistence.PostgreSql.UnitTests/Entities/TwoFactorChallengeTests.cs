using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class TwoFactorChallengeTests
{
    private static readonly DateTime _now = new(
        2026,
        9,
        8,
        12,
        0,
        0,
        DateTimeKind.Utc);
    private readonly Guid _memberId = Guid.CreateVersion7();
    private readonly Guid _credentialId = Guid.CreateVersion7();
    private readonly TwoFactorChallenge _challenge;

    public TwoFactorChallengeTests()
    {
        _challenge = CreateSignIn(_credentialId);
    }

    [Theory]
    [InlineData(true, TwoFactorRequiredAction.Verify)]
    [InlineData(false, TwoFactorRequiredAction.Enroll)]
    public void CreateSignIn_WhenFirstFactorIsProved_BindsIdentityAndOriginalExpiration(
        bool enrolled,
        TwoFactorRequiredAction expectedAction)
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        byte[] flowHash = [1];
        byte[] stampHash = [2];

        // Act
        var challenge = TwoFactorChallenge.CreateSignIn(
            id,
            _memberId,
            flowHash,
            enrolled ? _credentialId : null,
            stampHash,
            true,
            sessionId,
            _now);

        // Assert
        Assert.Equal(
            id,
            challenge.Id);
        Assert.Equal(
            _memberId,
            challenge.MemberId);
        Assert.Equal(
            flowHash,
            challenge.FlowHash);
        Assert.Equal(
            stampHash,
            challenge.SecurityStampHash);
        Assert.Equal(
            TwoFactorFlowPurpose.SignIn,
            challenge.Purpose);
        Assert.Equal(
            expectedAction,
            challenge.RequiredAction);
        Assert.Equal(
            _now,
            challenge.CreatedAt);
        Assert.Equal(
            _now.AddMinutes(5),
            challenge.ExpiresAt);
        Assert.Equal(
            sessionId,
            challenge.PreviousSessionId);
        Assert.True(challenge.IsPersistent);
        Assert.Null(challenge.ConsumedAt);
        Assert.Null(challenge.InvalidatedAt);
        Assert.Null(challenge.ManagementSessionId);
        Assert.Null(challenge.ResultAccessTokenId);
        Assert.Null(challenge.ResultSessionId);
    }

    [Fact]
    public void ConfirmVerification_WhenCurrentFactorIsVerified_PermitsOneCompletion()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7();
        var tokenId = Guid.CreateVersion7();
        _challenge.BindGoogleProof(
            Guid.CreateVersion7(),
            "encrypted provider identity");

        // Act
        _challenge.ConfirmVerification(_now.AddMinutes(1));
        _challenge.ConsumeSignIn(
            sessionId,
            tokenId,
            _now.AddMinutes(2));

        // Assert
        Assert.Equal(
            _now.AddMinutes(1),
            _challenge.VerifiedAt);
        Assert.Equal(
            _now.AddMinutes(2),
            _challenge.ConsumedAt);
        Assert.Equal(
            sessionId,
            _challenge.ResultSessionId);
        Assert.Equal(
            tokenId,
            _challenge.ResultAccessTokenId);
        Assert.False(_challenge.IsLive(_now.AddMinutes(3)));
        Assert.Null(_challenge.ProtectedGoogleContext);
        Assert.Throws<InvalidOperationException>(() => _challenge.ConsumeSignIn(
            sessionId,
            tokenId,
            _now.AddMinutes(3)));
    }

    [Fact]
    public void ConfirmSetup_WhenInitialAuthenticatorIsProved_AllowsCompletionWithoutReusingOtp()
    {
        // Arrange
        var challenge = CreateSignIn(null);
        challenge.StageAuthenticator(
            _credentialId,
            "encrypted new secret",
            _now.AddMinutes(1));

        // Act
        challenge.ConfirmSetup(_now.AddMinutes(2));

        // Assert
        Assert.Equal(
            _credentialId,
            challenge.ExpectedCredentialId);
        Assert.Equal(
            TwoFactorRequiredAction.Complete,
            challenge.RequiredAction);
        Assert.Equal(
            _now.AddMinutes(2),
            challenge.VerifiedAt);
        Assert.Equal(
            _now.AddMinutes(5),
            challenge.ExpiresAt);
        Assert.Null(challenge.PendingCredentialId);
        Assert.Null(challenge.PendingProtectedSecret);
        Assert.Throws<InvalidOperationException>(() => challenge.ConfirmSetup(_now.AddMinutes(3)));
    }

    [Fact]
    public void BeginRecovery_WhenCodeIsReserved_RequiresReplacementBeforeSignIn()
    {
        // Arrange
        var recoveryId = Guid.CreateVersion7();
        var replacementId = Guid.CreateVersion7();

        // Act
        _challenge.BeginRecovery(
            recoveryId,
            _now.AddMinutes(1));

        // Assert
        Assert.Equal(
            recoveryId,
            _challenge.ReservedRecoveryCodeId);
        Assert.Equal(
            TwoFactorRequiredAction.Replace,
            _challenge.RequiredAction);
        Assert.Null(_challenge.VerifiedAt);
        Assert.Throws<InvalidOperationException>(() => _challenge.ConsumeSignIn(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            _now.AddMinutes(1)));
        _challenge.StageAuthenticator(
            replacementId,
            "encrypted replacement",
            _now.AddMinutes(2));
        _challenge.ConfirmSetup(_now.AddMinutes(3));
        Assert.Equal(
            replacementId,
            _challenge.ExpectedCredentialId);
        Assert.Equal(
            _now.AddMinutes(5),
            _challenge.ExpiresAt);
    }

    [Theory]
    [InlineData(TwoFactorFlowPurpose.ReplaceAuthenticator, TwoFactorRequiredAction.Replace)]
    [InlineData(TwoFactorFlowPurpose.RegenerateRecoveryCodes, TwoFactorRequiredAction.Complete)]
    public void AuthorizeManagement_WhenFactorWasVerified_BindsOperationAndSession(
        TwoFactorFlowPurpose purpose,
        TwoFactorRequiredAction expectedAction)
    {
        // Arrange
        var sessionId = Guid.CreateVersion7();

        // Act
        _challenge.AuthorizeManagement(
            purpose,
            sessionId,
            _now);

        // Assert
        Assert.Equal(
            purpose,
            _challenge.Purpose);
        Assert.Equal(
            sessionId,
            _challenge.ManagementSessionId);
        Assert.Equal(
            expectedAction,
            _challenge.RequiredAction);
        Assert.Equal(
            _now,
            _challenge.VerifiedAt);
        Assert.Throws<InvalidOperationException>(() => _challenge.ConsumeSignIn(
            sessionId,
            Guid.CreateVersion7(),
            _now));
        Assert.Throws<InvalidOperationException>(() => _challenge.BeginRecovery(
            Guid.CreateVersion7(),
            _now));
        Assert.Throws<InvalidOperationException>(() => _challenge.ConfirmVerification(_now));
    }

    [Fact]
    public void AuthorizeManagement_WhenPurposeIsSignIn_RejectsInterchangeableGrant()
    {
        // Arrange
        // Act
        Assert.Throws<InvalidOperationException>(() => _challenge.AuthorizeManagement(
            TwoFactorFlowPurpose.SignIn,
            Guid.CreateVersion7(),
            _now));

        // Assert
        Assert.Null(_challenge.ManagementSessionId);
    }

    [Fact]
    public void ConsumeManagement_WhenRecoveryCodeRegenerationWasAuthorized_ConsumesGrantWithoutSession()
    {
        // Arrange
        _challenge.AuthorizeManagement(
            TwoFactorFlowPurpose.RegenerateRecoveryCodes,
            Guid.CreateVersion7(),
            _now);

        // Act
        _challenge.ConsumeManagement(_now.AddMinutes(1));

        // Assert
        Assert.Equal(
            _now.AddMinutes(1),
            _challenge.ConsumedAt);
        Assert.Null(_challenge.ResultSessionId);
        Assert.Null(_challenge.ResultAccessTokenId);
    }

    [Fact]
    public void ConsumeManagement_WhenReplacementIsUnconfirmed_RejectsConsumption()
    {
        // Arrange
        _challenge.AuthorizeManagement(
            TwoFactorFlowPurpose.ReplaceAuthenticator,
            Guid.CreateVersion7(),
            _now);

        // Act
        Assert.Throws<InvalidOperationException>(() => _challenge.ConsumeManagement(_now));

        // Assert
        Assert.Null(_challenge.ConsumedAt);
    }

    [Fact]
    public void ConsumeManagement_WhenGrantIsForSignIn_RejectsConsumption()
    {
        // Arrange
        _challenge.ConfirmVerification(_now);

        // Act
        Assert.Throws<InvalidOperationException>(() => _challenge.ConsumeManagement(_now));

        // Assert
        Assert.Null(_challenge.ConsumedAt);
    }

    [Fact]
    public void StageAuthenticator_WhenFlowRequiresVerification_RejectsReplacingCurrentFactor()
    {
        // Arrange
        // Act
        Assert.Throws<InvalidOperationException>(() => _challenge.StageAuthenticator(
            Guid.CreateVersion7(),
            "encrypted secret",
            _now));

        // Assert
        Assert.Null(_challenge.PendingCredentialId);
        Assert.Null(_challenge.PendingProtectedSecret);
    }

    [Fact]
    public void ConfirmSetup_WhenNoCandidateExists_RejectsConfirmation()
    {
        // Arrange
        var challenge = CreateSignIn(null);

        // Act
        Assert.Throws<InvalidOperationException>(() => challenge.ConfirmSetup(_now));

        // Assert
        Assert.Null(challenge.VerifiedAt);
    }

    [Fact]
    public void BeginRecovery_WhenEnrollmentIsRequired_RejectsRecovery()
    {
        // Arrange
        var challenge = CreateSignIn(null);

        // Act
        Assert.Throws<InvalidOperationException>(() => challenge.BeginRecovery(
            Guid.CreateVersion7(),
            _now));

        // Assert
        Assert.Null(challenge.ReservedRecoveryCodeId);
        Assert.Throws<InvalidOperationException>(() => challenge.ConfirmVerification(_now));
    }

    [Fact]
    public void Invalidate_WhenFlowHasSensitiveStagedMaterial_ClearsMaterialWithoutLosingReceiptIdentity()
    {
        // Arrange
        var challenge = CreateSignIn(null);
        var googleFlowId = Guid.CreateVersion7();
        challenge.BindGoogleProof(
            googleFlowId,
            "encrypted identity");
        challenge.StageAuthenticator(
            _credentialId,
            "encrypted candidate",
            _now);
        Assert.Equal(
            _credentialId,
            challenge.PendingCredentialId);
        Assert.Equal(
            "encrypted candidate",
            challenge.PendingProtectedSecret);
        Assert.Equal(
            "encrypted identity",
            challenge.ProtectedGoogleContext);

        // Act
        challenge.Invalidate(_now.AddMinutes(1));
        challenge.Invalidate(_now.AddMinutes(2));

        // Assert
        Assert.Equal(
            _now.AddMinutes(1),
            challenge.InvalidatedAt);
        Assert.Equal(
            googleFlowId,
            challenge.GoogleFlowId);
        Assert.Null(challenge.PendingCredentialId);
        Assert.Null(challenge.PendingProtectedSecret);
        Assert.Null(challenge.ProtectedGoogleContext);
        Assert.False(challenge.IsLive(_now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => challenge.StageAuthenticator(
            _credentialId,
            "another candidate",
            _now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(301, false)]
    public void IsLive_WhenExpirationBoundaryIsReached_UsesAbsoluteDeadline(
        int elapsedSeconds,
        bool expected)
    {
        // Arrange
        // Act
        var result = _challenge.IsLive(_now.AddSeconds(elapsedSeconds));

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    private TwoFactorChallenge CreateSignIn(Guid? credentialId)
    {

        return TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(),
            _memberId,
            new byte[32],
            credentialId,
            new byte[32],
            false,
            null,
            _now);
    }
}
