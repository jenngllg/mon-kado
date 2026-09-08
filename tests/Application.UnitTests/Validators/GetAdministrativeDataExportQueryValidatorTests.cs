using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetAdministrativeDataExportQueryValidatorTests
{
    private readonly GetAdministrativeDataExportQueryValidator _validator = new();
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Validate_WhenIdentifiersVary_EnforcesRouteContract(
        bool validIdentity,
        bool nullExport)
    {
        // Arrange
        var request = new GetAdministrativeDataExportQuery(
            validIdentity ? Guid.CreateVersion7() : Guid.Empty,
            validIdentity ? Guid.CreateVersion7() : null,
            nullExport ? null : Guid.CreateVersion7());

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            validIdentity,
            result.IsValid);
    }

    [Fact]
    public void Validate_WhenRouteIdentifiersAreEmpty_RejectsBothIdentifiers()
    {
        // Arrange
        var request = new GetAdministrativeDataExportQuery(
            Guid.CreateVersion7(),
            Guid.Empty,
            Guid.Empty);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.Equal(
            2,
            result.Errors.Count);
    }
}
