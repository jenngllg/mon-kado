using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetAdministrativeAuditEventsQueryValidatorTests
{
    private readonly GetAdministrativeAuditEventsQueryValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenSeveralFiltersAreInvalid_CollectsAllFailures()
    {
        // Arrange
        var query = new GetAdministrativeAuditEventsQuery
        {
            CallerId = Guid.Empty,
            Action = (AdministrativeAuditAction)99,
            AdministratorId = Guid.Empty,
            MemberId = Guid.Empty,
            WishlistId = Guid.Empty,
            ExportId = Guid.Empty,
            Page = 0,
            PageSize = 101,
            RequestReference = " ",
            From = "bad",
            To = "bad"
        };

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(
            11,
            result.Errors.Count);
    }

    [Theory]
    [InlineData(AdministrativeAuditAction.WishlistSuspended)]
    [InlineData(AdministrativeAuditAction.WishlistSuspensionReasonUpdated)]
    [InlineData(AdministrativeAuditAction.WishlistReactivated)]
    [InlineData(AdministrativeAuditAction.MemberDataExportRequested)]
    [InlineData(AdministrativeAuditAction.MemberDataExportDownloadStarted)]
    [InlineData(AdministrativeAuditAction.MemberErased)]
    public async Task ValidateAsync_WhenRecognizedActionAndMaximumReference_Accepts(AdministrativeAuditAction action)
    {
        // Arrange
        var query = new GetAdministrativeAuditEventsQuery
        {
            CallerId = Guid.CreateVersion7(),
            Action = action,
            RequestReference = new string(
                'a',
                128),
            Page = 1,
            PageSize = 100
        };

        // Act
        var result = await _validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }
}
