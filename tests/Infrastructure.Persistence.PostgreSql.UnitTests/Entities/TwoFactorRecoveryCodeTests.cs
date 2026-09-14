using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class TwoFactorRecoveryCodeTests
{
    private static readonly DateTime _now = new(
        2026,
        9,
        8,
        12,
        0,
        0,
        DateTimeKind.Utc);
    private readonly Guid _id = Guid.CreateVersion7();
    private readonly Guid _memberId = Guid.CreateVersion7();
    private readonly Guid _credentialId = Guid.CreateVersion7();
    private readonly Guid _challengeId = Guid.CreateVersion7();
    private readonly byte[] _hash = RandomNumberGenerator.GetBytes(32);
    private readonly TwoFactorRecoveryCode _code;

    public TwoFactorRecoveryCodeTests()
    {
        _code = TwoFactorRecoveryCode.Create(
            _id,
            _memberId,
            _credentialId,
            _hash);
    }

    [Fact]
    public void Create_WhenCodeIsIssued_RetainsOnlyHashAndIdentifiers()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal(
            _id,
            _code.Id);
        Assert.Equal(
            _memberId,
            _code.MemberId);
        Assert.Equal(
            _credentialId,
            _code.CredentialId);
        Assert.Equal(
            _hash,
            _code.CodeHash);
        Assert.Null(_code.ReservedChallengeId);
        Assert.Null(_code.ReservedUntil);
        Assert.Null(_code.ConsumedAt);
    }

    [Fact]
    public void TryReserve_WhenCodeIsAvailable_ReservesWithoutConsumptionOrExtension()
    {
        // Arrange
        var expiry = _now.AddMinutes(5);

        // Act
        var first = _code.TryReserve(
            _challengeId,
            expiry,
            _now);
        var repeated = _code.TryReserve(
            _challengeId,
            expiry.AddMinutes(5),
            _now.AddMinutes(1));

        // Assert
        Assert.True(first);
        Assert.True(repeated);
        Assert.Equal(
            _challengeId,
            _code.ReservedChallengeId);
        Assert.Equal(
            expiry,
            _code.ReservedUntil);
        Assert.Null(_code.ConsumedAt);
    }

    [Fact]
    public void TryReserve_WhenAnotherLiveFlowOwnsCode_RejectsConcurrentRecovery()
    {
        // Arrange
        _code.TryReserve(
            _challengeId,
            _now.AddMinutes(5),
            _now);

        // Act
        var result = _code.TryReserve(
            Guid.CreateVersion7(),
            _now.AddMinutes(5),
            _now);

        // Assert
        Assert.False(result);
        Assert.Equal(
            _challengeId,
            _code.ReservedChallengeId);
    }

    [Fact]
    public void TryReserve_WhenAbandonedFlowExpires_ReleasesReservationForNewFlow()
    {
        // Arrange
        _code.TryReserve(
            _challengeId,
            _now.AddMinutes(5),
            _now);
        var nextChallenge = Guid.CreateVersion7();

        // Act
        var result = _code.TryReserve(
            nextChallenge,
            _now.AddMinutes(10),
            _now.AddMinutes(5));

        // Assert
        Assert.True(result);
        Assert.Equal(
            nextChallenge,
            _code.ReservedChallengeId);
        Assert.Null(_code.ConsumedAt);
    }

    [Fact]
    public void TryReserve_WhenChallengeIsExpired_RejectsReservation()
    {
        // Arrange
        // Act
        var result = _code.TryReserve(
            _challengeId,
            _now,
            _now);

        // Assert
        Assert.False(result);
        Assert.Null(_code.ReservedChallengeId);
    }

    [Fact]
    public void TryConsume_WhenReplacementConfirms_ConsumesCodeExactlyOnce()
    {
        // Arrange
        _code.TryReserve(
            _challengeId,
            _now.AddMinutes(5),
            _now);

        // Act
        var first = _code.TryConsume(
            _challengeId,
            _now.AddMinutes(1));
        var replay = _code.TryConsume(
            _challengeId,
            _now.AddMinutes(2));
        var reservation = _code.TryReserve(
            Guid.CreateVersion7(),
            _now.AddMinutes(20),
            _now.AddMinutes(10));

        // Assert
        Assert.True(first);
        Assert.False(replay);
        Assert.False(reservation);
        Assert.Equal(
            _now.AddMinutes(1),
            _code.ConsumedAt);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TryConsume_WhenReservationIsUnavailable_DoesNotConsume(
        bool reserve,
        bool expire)
    {
        // Arrange

        if (reserve)
            _code.TryReserve(
                _challengeId,
                _now.AddMinutes(5),
                _now);

        // Act
        var result = _code.TryConsume(
            expire ? _challengeId : Guid.CreateVersion7(),
            expire ? _now.AddMinutes(5) : _now);

        // Assert
        Assert.False(result);
        Assert.Null(_code.ConsumedAt);
    }
}
