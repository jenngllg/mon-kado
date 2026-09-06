using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpsertProfileImageCommandValidatorTests
{
    private readonly UpsertProfileImageCommandValidator _validator = new();
    [Theory]
    [InlineData(-1, true, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, true)]
    [InlineData(1, false, false)]
    [InlineData(GiftImageConstraints.MaximumInputLength, true, true)]
    [InlineData(GiftImageConstraints.MaximumInputLength + 1, true, false)]
    public async Task ValidateAsync_WhenImageShapeAndLengthAreChecked_UsesTheImagePropertyPath(
        int length,
        bool validShape,
        bool expectedValid)
    {
        // Arrange
        var command = new UpsertProfileImageCommand(
            Guid.CreateVersion7(),
            length < 0 ? null : new byte[length],
            42,
            validShape);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedValid,
            result.IsValid);
        Assert.All(
            result.Errors,
            error => Assert.Equal(
                "Image",
                error.PropertyName));
    }
}
