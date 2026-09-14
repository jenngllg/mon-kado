using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetTwoFactorStatusQueryValidatorTests
{
    private readonly GetTwoFactorStatusQueryValidator _validator = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenAuthenticatedIdentifierIsChecked_RejectsEmptyIdentifier(bool valid)
    {
        // Arrange
        var request = new GetTwoFactorStatusQuery(valid ? Guid.CreateVersion7() : Guid.Empty);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
    }
}
