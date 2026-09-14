using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class MemberTwoFactorTests
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
    private readonly MemberTwoFactor _factor;

    public MemberTwoFactorTests()
    {
        _factor = MemberTwoFactor.Create(_memberId);
    }

    [Fact]
    public void Create_WhenStateIsNew_DoesNotEnableAuthenticator()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal(
            _memberId,
            _factor.MemberId);
        Assert.Null(_factor.CredentialId);
        Assert.Null(_factor.ProtectedSecret);
        Assert.Null(_factor.EnabledAt);
        Assert.Null(_factor.LastAcceptedTimeStep);
        Assert.Null(_factor.LockedUntil);
        Assert.Null(_factor.VerificationWindowStartedAt);
        Assert.Equal(
            0,
            _factor.FailedAttempts);
        Assert.Equal(
            0,
            _factor.VerificationCount);
    }

    [Fact]
    public void TryStartVerification_WhenMinuteQuotaIsExhausted_RejectsUntilNextWindow()
    {
        // Arrange
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.True(_factor.TryStartVerification(_now));

        // Act
        var rejected = _factor.TryStartVerification(_now.AddSeconds(59));
        var accepted = _factor.TryStartVerification(_now.AddMinutes(1));

        // Assert
        Assert.False(rejected);
        Assert.True(accepted);
        Assert.Equal(
            _now.AddMinutes(1),
            _factor.VerificationWindowStartedAt);
        Assert.Equal(
            1,
            _factor.VerificationCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void RecordFailure_WhenBelowLockoutThreshold_PreservesFailuresAcrossAttempts(int failures)
    {
        // Arrange
        for (var attempt = 0; attempt < failures; attempt++)
            _factor.RecordFailure(_now);

        // Act
        var accepted = _factor.TryStartVerification(_now.AddMinutes(1));

        // Assert
        Assert.True(accepted);
        Assert.Null(_factor.LockedUntil);
        Assert.Equal(
            failures,
            _factor.FailedAttempts);
    }

    [Fact]
    public void RecordFailure_WhenFifthFailureOccurs_LocksAccountForFifteenMinutes()
    {
        // Arrange
        for (var attempt = 0; attempt < 5; attempt++)
            _factor.RecordFailure(_now);

        // Act
        var rejected = _factor.TryStartVerification(_now.AddMinutes(14));

        // Assert
        Assert.False(rejected);
        Assert.Equal(
            _now.AddMinutes(15),
            _factor.LockedUntil);
        Assert.Equal(
            5,
            _factor.FailedAttempts);
    }

    [Fact]
    public void TryStartVerification_WhenLockoutExpires_AllowsFreshAttempts()
    {
        // Arrange
        for (var attempt = 0; attempt < 5; attempt++)
            _factor.RecordFailure(_now);

        // Act
        var accepted = _factor.TryStartVerification(_now.AddMinutes(15));

        // Assert
        Assert.True(accepted);
        Assert.Null(_factor.LockedUntil);
        Assert.Equal(
            0,
            _factor.FailedAttempts);
    }

    [Fact]
    public void AcceptTimeStep_WhenVerifiedStepIsNew_ConsumesItAndResetsFailures()
    {
        // Arrange
        _factor.RecordFailure(_now);

        // Act
        _factor.AcceptTimeStep(100);
        _factor.AcceptTimeStep(101);

        // Assert
        Assert.Equal(
            101L,
            _factor.LastAcceptedTimeStep);
        Assert.Equal(
            0,
            _factor.FailedAttempts);
        Assert.Null(_factor.LockedUntil);
    }

    [Theory]
    [InlineData(99L)]
    [InlineData(100L)]
    public void AcceptTimeStep_WhenStepIsNotNew_RejectsReplay(long timeStep)
    {
        // Arrange
        _factor.AcceptTimeStep(100);

        // Act
        Assert.Throws<InvalidOperationException>(() => _factor.AcceptTimeStep(timeStep));

        // Assert
        Assert.Equal(
            100L,
            _factor.LastAcceptedTimeStep);
    }

    [Fact]
    public void ConfirmAuthenticator_WhenNewCredentialIsProved_ReplacesAllCredentialState()
    {
        // Arrange
        _factor.ConfirmAuthenticator(
            Guid.CreateVersion7(),
            "old encrypted secret",
            100,
            _now);
        _factor.RecordFailure(_now);
        var credentialId = Guid.CreateVersion7();

        // Act
        _factor.ConfirmAuthenticator(
            credentialId,
            "new encrypted secret",
            99,
            _now.AddSeconds(1));

        // Assert
        Assert.Equal(
            credentialId,
            _factor.CredentialId);
        Assert.Equal(
            "new encrypted secret",
            _factor.ProtectedSecret);
        Assert.Equal(
            _now.AddSeconds(1),
            _factor.EnabledAt);
        Assert.Equal(
            99L,
            _factor.LastAcceptedTimeStep);
        Assert.Equal(
            0,
            _factor.FailedAttempts);
        Assert.Null(_factor.LockedUntil);
    }
}
