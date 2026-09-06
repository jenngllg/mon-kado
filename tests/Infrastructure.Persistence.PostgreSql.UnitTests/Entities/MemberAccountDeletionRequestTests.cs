using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Entities;

public class MemberAccountDeletionRequestTests
{
    private static readonly DateTime _createdAt = new(
        2026,
        9,
        6,
        12,
        0,
        0,
        DateTimeKind.Utc);
    [Fact]
    public void Constructor_WhenCreated_CapturesSecurityStateAndAbsoluteDeadline()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();

        // Act
        var request = new MemberAccountDeletionRequest(
            memberId,
            "member@example.test",
            "stamp",
            _createdAt,
            TimeSpan.FromMinutes(30));

        // Assert
        Assert.Equal(
            7,
            request.Id.Version);
        Assert.Equal(
            memberId,
            request.MemberId);
        Assert.Equal(
            "member@example.test",
            request.Email);
        Assert.Equal(
            "stamp",
            request.SecurityStamp);
        Assert.Equal(
            _createdAt,
            request.CreatedAt);
        Assert.Equal(
            _createdAt.AddMinutes(30),
            request.ExpiresAt);
    }

    [Theory]
    [InlineData("member@example.test", "stamp", 29, true)]
    [InlineData("MEMBER@example.test", "stamp", 29, true)]
    [InlineData("member@example.test", "stamp", 30, false)]
    [InlineData("member@example.test", "stamp", 31, false)]
    [InlineData("changed@example.test", "stamp", 1, false)]
    [InlineData("member@example.test", "changed", 1, false)]
    [InlineData(null, "stamp", 1, false)]
    [InlineData("member@example.test", null, 1, false)]
    public void IsValid_WhenSecurityStateOrTimeChanges_ReturnsExpectedEligibility(
        string? email,
        string? stamp,
        int elapsedMinutes,
        bool expected)
    {
        // Arrange
        var request = new MemberAccountDeletionRequest(
            Guid.CreateVersion7(),
            "member@example.test",
            "stamp",
            _createdAt,
            TimeSpan.FromMinutes(30));

        // Act
        var actual = request.IsValid(
            email,
            stamp,
            _createdAt.AddMinutes(elapsedMinutes));

        // Assert
        Assert.Equal(
            expected,
            actual);
    }
}
