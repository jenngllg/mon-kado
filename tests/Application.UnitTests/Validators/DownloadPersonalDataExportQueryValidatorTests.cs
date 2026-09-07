using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class DownloadPersonalDataExportQueryValidatorTests
{
    private readonly DownloadPersonalDataExportQueryValidator _validator = new();
    [Theory]
    [InlineData(false, "absent")]
    [InlineData(false, "empty")]
    [InlineData(false, "valid")]
    [InlineData(true, "absent")]
    [InlineData(true, "empty")]
    [InlineData(true, "valid")]
    public void Validate_WhenIdentitiesVary_ReportsEveryInvalidIdentity(
        bool validMember,
        string identifier)
    {
        // Arrange
        var memberId = validMember ? Guid.CreateVersion7() : Guid.Empty;
        var exportId = identifier switch
        {
            "absent" => (Guid?)null,
            "empty" => Guid.Empty,
            _ => Guid.CreateVersion7()
        };
        var request = new DownloadPersonalDataExportQuery(
            memberId,
            exportId);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            !validMember,
            result.Errors.Any(error => error.PropertyName == "MemberId"));
        Assert.Equal(
            identifier != "valid",
            result.Errors.Any(error => error.PropertyName == "ExportId"));
    }
}
