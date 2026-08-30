using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpsertGiftReservationCommandValidatorTests
{
    private readonly UpsertGiftReservationCommandValidator _validator = new();

    [Theory]
    [InlineData(false, false, false, false, 1, true)]
    [InlineData(false, false, false, false, 100, true)]
    [InlineData(true, false, false, false, 1, false)]
    [InlineData(false, true, false, false, 1, false)]
    [InlineData(false, false, true, false, 1, false)]
    [InlineData(false, false, false, true, 1, false)]
    [InlineData(false, false, false, false, null, false)]
    [InlineData(false, false, false, false, 0, false)]
    [InlineData(false, false, false, false, 101, false)]
    public async Task ValidateAsync_WhenValuesVary_ReturnsExpectedResult(
        bool shareLinkIdIsEmpty,
        bool secretIsMissing,
        bool wishIdIsEmpty,
        bool memberIdIsEmpty,
        int? quantity,
        bool expectedIsValid)
    {
        // Arrange
        var command = new UpsertGiftReservationCommand(
            shareLinkIdIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            secretIsMissing
                ? null
                : "secret",
            wishIdIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            memberIdIsEmpty
                ? Guid.Empty
                : null,
            "guest",
            quantity,
            null);

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
