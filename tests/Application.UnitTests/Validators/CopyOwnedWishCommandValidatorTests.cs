using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CopyOwnedWishCommandValidatorTests
{
    private readonly CopyOwnedWishCommandValidator _validator = new();

    [Theory]
    [InlineData("owner")]
    [InlineData("source-list")]
    [InlineData("source-wish")]
    [InlineData("destination")]
    [InlineData("missing-destination")]
    [InlineData("same-list")]
    [InlineData("valid")]
    public void Validate_WhenIdentifiersAreProvided_RequiresDistinctNonEmptyLists(string scenario)
    {
        // Arrange
        var source = Guid.CreateVersion7();
        Guid? destination = Guid.CreateVersion7();

        if (scenario == "missing-destination")
            destination = null;

        if (scenario == "destination")
            destination = Guid.Empty;

        if (scenario == "same-list")
            destination = source;

        var request = new CopyOwnedWishCommand(
            scenario == "owner" ? Guid.Empty : Guid.CreateVersion7(),
            scenario == "source-list" ? Guid.Empty : source,
            scenario == "source-wish" ? Guid.Empty : Guid.CreateVersion7(),
            destination);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            scenario == "valid",
            result.IsValid);
    }
}
