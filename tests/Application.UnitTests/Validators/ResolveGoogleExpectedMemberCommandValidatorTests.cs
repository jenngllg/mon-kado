using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ResolveGoogleExpectedMemberCommandValidatorTests
{
    private readonly ResolveGoogleExpectedMemberCommandValidator _validator;

    public ResolveGoogleExpectedMemberCommandValidatorTests()
    {
        _validator = new ResolveGoogleExpectedMemberCommandValidator();
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentityIsValid_ReturnsValid()
    {
        // Arrange
        var command = new ResolveGoogleExpectedMemberCommand(new GoogleIdentity(
            "subject",
            "member@gmail.com",
            true,
            null,
            null));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentityIsMissing_ReturnsIdentityFailure()
    {
        // Arrange
        var command = new ResolveGoogleExpectedMemberCommand(null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Identity));
    }
}
