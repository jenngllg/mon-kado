using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class DeleteWishlistSubscriptionCommandValidatorTests
{
    private readonly DeleteWishlistSubscriptionCommandValidator _validator = new();

    [Theory]
    [InlineData("valid")]
    [InlineData("member")]
    [InlineData("id")]
    public void Validate_WhenInputsAreProvided_ValidatesAllRequiredValues(string scenario)
    {
        // Arrange
        var memberId = scenario == "member" ? Guid.Empty : Guid.CreateVersion7();
        var id = scenario == "id" ? Guid.Empty : Guid.CreateVersion7();
        var request = new DeleteWishlistSubscriptionCommand(
            memberId,
            id);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            scenario == "valid",
            result.IsValid);
    }
}
