using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CreateWishImportPreviewCommandValidatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenRequestIsChecked_ReportsAllInvalidProperties(bool valid)
    {
        // Arrange
        var command = valid ? WishImportTestData.CreateValidCommand() : new CreateWishImportPreviewCommand(
            Guid.Empty,
            Guid.Empty,
            null);
        var validator = new CreateWishImportPreviewCommandValidator();

        // Act
        var result = await validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
        Assert.Equal(
            valid ? 0 : 3,
            result.Errors.Count);
    }
}
