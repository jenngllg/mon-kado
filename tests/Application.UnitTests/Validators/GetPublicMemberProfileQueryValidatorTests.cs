using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GetPublicMemberProfileQueryValidatorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenIdentifierIsProvided_RequiresNonemptyIdentifier(bool valid)
    {
        // Arrange
        var validator = new GetPublicMemberProfileQueryValidator();
        var query = new GetPublicMemberProfileQuery(valid ? Guid.CreateVersion7() : Guid.Empty);

        // Act
        var result = await validator.ValidateAsync(
            query,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            valid,
            result.IsValid);
    }
}
