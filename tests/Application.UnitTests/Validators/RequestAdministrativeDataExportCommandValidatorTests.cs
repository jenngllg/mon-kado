using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RequestAdministrativeDataExportCommandValidatorTests
{
    private readonly RequestAdministrativeDataExportCommandValidator _validator = new();
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("SUPPORT-807", true)]
    [InlineData("  SUPPORT-807  ", true)]
    [InlineData("SUPPORT\n807", false)]
    [InlineData("SUPPORT\u0000807", false)]
    [InlineData("\tSUPPORT-807", false)]
    public void Validate_WhenReferenceVaries_RequiresNonblankControlFreeReference(
        string? reference,
        bool expected)
    {
        // Arrange
        var request = new RequestAdministrativeDataExportCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            reference);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            expected,
            result.IsValid);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Validate_WhenTrimmedLengthVaries_EnforcesMaximum(
        int length,
        bool expected)
    {
        // Arrange
        var reference = new string(
            'A',
            length);
        var request = new RequestAdministrativeDataExportCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"  {reference}  ");

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            expected,
            result.IsValid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_WhenIdentitiesAreMissing_AggregatesErrors(bool nullTarget)
    {
        // Arrange
        var request = new RequestAdministrativeDataExportCommand(
            Guid.Empty,
            nullTarget ? null : Guid.Empty,
            "SUPPORT-807");

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            2,
            result.Errors.Count);
    }
}
