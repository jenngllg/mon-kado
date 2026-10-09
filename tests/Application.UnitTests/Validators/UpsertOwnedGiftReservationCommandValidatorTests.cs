using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpsertOwnedGiftReservationCommandValidatorTests
{
    private readonly UpsertOwnedGiftReservationCommandValidator _validator = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_WhenIdentifiersVary_AggregatesEveryMissingIdentifier(bool empty)
    {
        // Arrange
        var request = new UpsertOwnedGiftReservationCommand(
            empty ? Guid.Empty : Guid.CreateVersion7(),
            empty ? Guid.Empty : Guid.CreateVersion7(),
            empty ? Guid.Empty : Guid.CreateVersion7(),
            1,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            !empty,
            result.IsValid);
        Assert.Equal(
            empty ? 3 : 0,
            result.Errors.Count);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    public async Task ValidateAsync_WhenQuantityVaries_EnforcesSharedBounds(
        int? quantity,
        bool expected)
    {
        // Arrange
        var request = new UpsertOwnedGiftReservationCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            quantity,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expected,
            result.IsValid);
    }
}
