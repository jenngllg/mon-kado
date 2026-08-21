using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class EmailConfirmationServiceInputTests
{
    private static readonly DateTime _now = new(
        2026,
        8,
        21,
        12,
        0,
        0,
        DateTimeKind.Utc);

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, null, false)]
    public void IsExpiredUnconfirmedAccount_WhenAccountIsProvided_ReturnsExpectedResult(
        bool emailConfirmed,
        int? expirationOffsetDays,
        bool expected)
    {
        // Arrange
        var user = new MonKadoUser
        {
            EmailConfirmed = emailConfirmed,
            UnconfirmedAccountExpiresAt = expirationOffsetDays is null
                ? null
                : _now.AddDays(expirationOffsetDays.Value)
        };

        // Act
        var result = EmailConfirmationService.IsExpiredUnconfirmedAccount(
            user,
            _now);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Theory]
    [InlineData("invalid", "token")]
    [InlineData("00000000-0000-0000-0000-000000000000", "token")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "A")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "!!!")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "_w")]
    public async Task ConfirmAsync_WhenInputCannotBeDecoded_ReturnsFalse(
        string userId,
        string token)
    {
        // Arrange
        var service = CreateService(new StubLookupNormalizer(null));

        // Act
        var confirmed = await service.ConfirmAsync(
            userId,
            token,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(confirmed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestAsync_WhenNormalizedEmailIsBlank_CompletesWithoutPersistence(
        string? normalizedEmail)
    {
        // Arrange
        var normalizer = new StubLookupNormalizer(normalizedEmail);
        var service = CreateService(normalizer);

        // Act
        await service.RequestAsync(
            "user@example.com",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            normalizer.NormalizeEmailCallCount);
    }

    private static EmailConfirmationService CreateService(StubLookupNormalizer normalizer)
    {
        return new(
            null!,
            null!,
            null!,
            null!,
            null!,
            normalizer,
            TimeProvider.System);
    }
}
