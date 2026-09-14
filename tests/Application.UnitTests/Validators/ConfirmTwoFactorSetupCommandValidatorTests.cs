using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ConfirmTwoFactorSetupCommandValidatorTests
{
    private const string Flow = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly ConfirmTwoFactorSetupCommandValidator _validator = new();

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
        var request = new ConfirmTwoFactorSetupCommand(
            flow,
            "123456");

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expected,
            result.IsValid);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("123456", true)]
    [InlineData("000000", true)]
    [InlineData("123456\n", false)]
    public async Task ValidateAsync_WhenConfirmationCodeVaries_RequiresSixDigits(
        string? code,
        bool expected)
    {
        // Arrange
        var request = new ConfirmTwoFactorSetupCommand(
            Flow,
            code);

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
