using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RegenerateTwoFactorRecoveryCodesCommandValidatorTests
{
    private readonly RegenerateTwoFactorRecoveryCodesCommandValidator _validator = new();

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("short", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", false)]
    public async Task ValidateAsync_WhenFlowEncodingVaries_RequiresCanonicalProof(
        string? flow,
        bool expected)
    {
        // Arrange
        var request = new RegenerateTwoFactorRecoveryCodesCommand(
            flow);

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
