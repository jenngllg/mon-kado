using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class JoinSharedWishlistCommandValidatorTests
{
    private readonly JoinSharedWishlistCommandValidator _validator = new();

    [Theory]
    [InlineData(false, null, "Jenn", true)]
    [InlineData(false, null, null, false)]
    [InlineData(false, "00000000-0000-0000-0000-000000000000", null, false)]
    [InlineData(false, "0198e75d-8280-7000-8000-000000000001", null, true)]
    [InlineData(true, null, "Jenn", false)]
    public async Task ValidateAsync_WhenIdentityAndShareProofVary_ReturnsExpectedResult(
        bool shareProofIsMissing,
        string? memberId,
        string? displayName,
        bool expectedIsValid)
    {
        // Arrange
        var command = new JoinSharedWishlistCommand(
            shareProofIsMissing
                ? Guid.Empty
                : Guid.CreateVersion7(),
            shareProofIsMissing
                ? null
                : "secret",
            memberId is null
                ? null
                : Guid.Parse(memberId),
            null,
            displayName);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedIsValid,
            result.IsValid);
    }
}
