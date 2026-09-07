using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RequestPersonalDataExportCommandValidatorTests
{
    private readonly RequestPersonalDataExportCommandValidator _validator = new();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_WhenMemberIdentityVaries_RequiresANonemptyAuthenticatedIdentity(bool valid)
    {
        // Arrange
        var request = new RequestPersonalDataExportCommand(valid ? Guid.CreateVersion7() : Guid.Empty);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
        Assert.Equal(
            valid ? 0 : 1,
            result.Errors.Count);
    }
}
