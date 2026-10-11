using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CreateWishlistSubscriptionCommandValidatorTests
{
    private readonly CreateWishlistSubscriptionCommandValidator _validator = new();

    [Theory]
    [InlineData("valid")]
    [InlineData("member")]
    [InlineData("id")]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("long")]
    public void Validate_WhenInputsAreProvided_ValidatesAllRequiredValues(string scenario)
    {
        // Arrange
        var memberId = scenario == "member" ? Guid.Empty : Guid.CreateVersion7();
        var id = scenario == "id" ? Guid.Empty : Guid.CreateVersion7();
        string? secret = "secret";

        if (scenario == "empty")
            secret = string.Empty;

        if (scenario == "null")
            secret = null;

        if (scenario == "long")
            secret = new string(
                'x',
                513);
        var request = new CreateWishlistSubscriptionCommand(
            memberId,
            id,
            secret);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            scenario == "valid",
            result.IsValid);
    }
}
