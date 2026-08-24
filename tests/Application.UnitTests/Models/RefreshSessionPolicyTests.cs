using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Models;

public class RefreshSessionPolicyTests
{
    private static readonly DateTime _now = new(
        2030,
        1,
        1,
        0,
        0,
        0,
        DateTimeKind.Utc);

    [Theory]
    [InlineData(false, 8)]
    [InlineData(true, 720)]
    public void GetInitialExpiration_WhenPersistenceVaries_ReturnsExpectedLifetime(
        bool isPersistent,
        int expectedHours)
    {
        // Arrange
        var expected = _now.AddHours(expectedHours);

        // Act
        var result = RefreshSessionPolicy.GetInitialExpiration(
            _now,
            isPersistent);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetRotatedExpiration_WhenPersistenceVaries_PreservesExpectedPolicy(
        bool isPersistent)
    {
        // Arrange
        var currentExpiration = _now.AddDays(20);
        var expected = isPersistent
            ? currentExpiration
            : _now.AddHours(8);

        // Act
        var result = RefreshSessionPolicy.GetRotatedExpiration(
            _now,
            currentExpiration,
            isPersistent);

        // Assert
        Assert.Equal(
            expected,
            result);
    }
}
