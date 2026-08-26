using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetCurrentWishlistParticipantQueryValidatorTests
{
    private readonly GetCurrentWishlistParticipantQueryValidator _validator = new();

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "0198e75d-8280-7000-8000-000000000001", true)]
    [InlineData(false, "00000000-0000-0000-0000-000000000000", false)]
    [InlineData(true, null, false)]
    public async Task ValidateAsync_WhenValuesVary_ReturnsExpectedResult(
        bool shareProofIsMissing,
        string? memberId,
        bool expectedIsValid)
    {
        // Arrange
        var query = new GetCurrentWishlistParticipantQuery(
            shareProofIsMissing
                ? Guid.Empty
                : Guid.CreateVersion7(),
            shareProofIsMissing
                ? null
                : "secret",
            memberId is null
                ? null
                : Guid.Parse(memberId),
            "guest");

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedIsValid,
            result.IsValid);
    }
}
