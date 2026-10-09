using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetOwnedGiftReservationQueryValidatorTests
{
    private readonly GetOwnedGiftReservationQueryValidator _validator = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateAsync_WhenIdentifiersVary_AggregatesEveryMissingIdentifier(bool empty)
    {
        // Arrange
        var request = new GetOwnedGiftReservationQuery(
            empty ? Guid.Empty : Guid.CreateVersion7(),
            empty ? Guid.Empty : Guid.CreateVersion7(),
            empty ? Guid.Empty : Guid.CreateVersion7());

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

}
