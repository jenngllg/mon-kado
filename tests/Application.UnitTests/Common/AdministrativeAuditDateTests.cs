using JennGllg.Fr.MonKado.Back.Application.Common;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Common;

public class AdministrativeAuditDateTests
{
    [Theory]
    [InlineData("2026-09-01T12:00:00Z")]
    [InlineData("2026-09-01T12:00:00.0000000Z")]
    [InlineData("2026-09-01T14:00:00+02:00")]
    [InlineData("2026-09-01T14:00:00.0000000+02:00")]
    [InlineData("2026-09-01T07:00:00-05:00")]
    public void Parse_WhenTimezoneIsExplicit_NormalizesUtc(string input)
    {
        // Arrange
        var expected = new DateTime(
            2026,
            9,
            1,
            12,
            0,
            0,
            DateTimeKind.Utc);

        // Act
        var valid = AdministrativeAuditDate.IsValid(input);
        var actual = AdministrativeAuditDate.Parse(input);

        // Assert
        Assert.True(valid);
        Assert.Equal(
            expected,
            actual);
        Assert.Equal(
            DateTimeKind.Utc,
            actual.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("2026-09-01")]
    [InlineData("2026-09-01T12:00:00")]
    [InlineData("2026-02-30T12:00:00Z")]
    [InlineData("2026-09-01T12:00:00+25:00")]
    [InlineData("2026-09-01T12:00:00Z ")]
    public void IsValid_WhenDateIsAmbiguousOrInvalid_Rejects(string input)
    {
        // Arrange

        // Act
        var valid = AdministrativeAuditDate.IsValid(input);

        // Assert
        Assert.False(valid);
    }
}
