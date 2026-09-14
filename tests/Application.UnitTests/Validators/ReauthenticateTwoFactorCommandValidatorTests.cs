using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ReauthenticateTwoFactorCommandValidatorTests
{
    private readonly ReauthenticateTwoFactorCommandValidator _validator = new();

    [Theory]
    [InlineData(TwoFactorFlowPurpose.ReplaceAuthenticator, "123456", null, true)]
    [InlineData(TwoFactorFlowPurpose.RegenerateRecoveryCodes, "123456", null, true)]
    [InlineData(TwoFactorFlowPurpose.SignIn, "123456", null, false)]
    [InlineData(null, "123456", null, false)]
    [InlineData((TwoFactorFlowPurpose)99, "123456", null, false)]
    [InlineData(TwoFactorFlowPurpose.ReplaceAuthenticator, null, null, false)]
    [InlineData(TwoFactorFlowPurpose.ReplaceAuthenticator, "", null, false)]
    [InlineData(TwoFactorFlowPurpose.ReplaceAuthenticator, null, "", false)]
    [InlineData(TwoFactorFlowPurpose.ReplaceAuthenticator, null, "0123456789abcdef0123456789abcdef", true)]
    [InlineData(TwoFactorFlowPurpose.RegenerateRecoveryCodes, null, "0123456789abcdef0123456789abcdef", false)]
    [InlineData(TwoFactorFlowPurpose.ReplaceAuthenticator, "123456", "0123456789abcdef0123456789abcdef", false)]
    public async Task ValidateAsync_WhenPurposeAndProofVary_EnforcesOperationBoundReauthentication(
        TwoFactorFlowPurpose? purpose,
        string? code,
        string? recoveryCode,
        bool expected)
    {
        // Arrange
        var request = new ReauthenticateTwoFactorCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            purpose,
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

    [Fact]
    public async Task ValidateAsync_WhenAuthenticatedIdentifiersAreEmpty_ReportsBothErrors()
    {
        // Arrange
        var request = new ReauthenticateTwoFactorCommand(
            Guid.Empty,
            Guid.Empty,
            TwoFactorFlowPurpose.ReplaceAuthenticator,
            "123456",
            null);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            result.Errors.Count);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(request.MemberId));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(request.AccessTokenId));
    }
}
