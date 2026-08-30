using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CancelGiftReservationCommandValidatorTests
{
    private readonly CancelGiftReservationCommandValidator _validator = new();

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public async Task ValidateAsync_WhenValuesVary_ReturnsExpectedResult(
        bool shareLinkIdIsEmpty,
        bool secretIsMissing,
        bool wishIdIsEmpty,
        bool memberIdIsEmpty,
        bool expectedIsValid)
    {
        // Arrange
        var command = new CancelGiftReservationCommand(
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
            42);

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
