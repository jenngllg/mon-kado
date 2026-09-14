using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CompleteTwoFactorCommandValidatorTests
{
    private const string Flow = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private readonly CompleteTwoFactorCommandValidator _validator = new();

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
        var request = new CompleteTwoFactorCommand(
            flow,
            "123456",
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

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("123456", null, true)]
    [InlineData("000000", null, true)]
    [InlineData("", null, false)]
    [InlineData(" 123456", null, false)]
    [InlineData("１２３４５６", null, false)]
    [InlineData(null, "01234567-89ABCDEF-01234567-89ABCDEF", true)]
    [InlineData(null, "0123456789abcdef0123456789abcdef", true)]
    [InlineData(null, "", false)]
    [InlineData("123456", "0123456789abcdef0123456789abcdef", false)]
    public async Task ValidateAsync_WhenProofCombinationVaries_RejectsMalformedOrCompetingProofs(
        string? code,
        string? recoveryCode,
        bool expected)
    {
        // Arrange
        var request = new CompleteTwoFactorCommand(
            Flow,
            code,
            recoveryCode);

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
