using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CopyWishCommandValidatorTests
{
    private readonly CopyWishCommandValidator _validator = new();

    [Theory]
    [InlineData("ownerId")]
    [InlineData("wishlistId")]
    [InlineData("sourceShareLinkId")]
    [InlineData("sourceWishId")]
    public void Validate_WhenAnIdentifierIsEmpty_ReportsTheCorrespondingProperty(
        string property)
    {
        // Arrange
        var request = new CopyWishCommand(
            property == "ownerId" ? Guid.Empty : Guid.CreateVersion7(),
            property == "wishlistId" ? Guid.Empty : Guid.CreateVersion7(),
            property == "sourceShareLinkId" ? Guid.Empty : Guid.CreateVersion7(),
            property == "sourceWishId" ? Guid.Empty : Guid.CreateVersion7(),
            "secret");

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => string.Equals(
                error.PropertyName,
                property,
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WhenSourceIdentifiersAreMissing_AggregatesBothErrors()
    {
        // Arrange
        var request = new CopyWishCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            null,
            null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(CopyWishCommand.SourceShareLinkId));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(CopyWishCommand.SourceWishId));
    }

    [Fact]
    public void Validate_WhenIdentifiersAreValid_LeavesSecretAccessCheckingToTheCopyService()
    {
        // Arrange
        var request = new CopyWishCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null);

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }
}
