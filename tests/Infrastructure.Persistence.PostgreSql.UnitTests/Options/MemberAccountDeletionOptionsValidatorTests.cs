using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Options;

public class MemberAccountDeletionOptionsValidatorTests
{
    private readonly MemberAccountDeletionOptionsValidator _validator = new();
    [Fact]
    public void Validate_WhenDefaultsAreUsed_AcceptsPolicy()
    {
        // Arrange
        var options = new MemberAccountDeletionOptions();

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(
            TimeSpan.FromMinutes(30),
            options.Lifetime);
        Assert.Equal(
            TimeSpan.FromHours(1),
            options.RequestWindow);
        Assert.Equal(
            3,
            options.MaximumRequests);
    }

    [Theory]
    [InlineData(0, 60, 3)]
    [InlineData(61, 60, 3)]
    [InlineData(30, 0, 3)]
    [InlineData(30, 1441, 3)]
    [InlineData(30, 60, 0)]
    [InlineData(30, 60, 11)]
    public void Validate_WhenPolicyIsInvalid_RejectsConfiguration(
        int lifetime,
        int window,
        int maximumRequests)
    {
        // Arrange
        var options = new MemberAccountDeletionOptions
        {
            Lifetime = TimeSpan.FromMinutes(lifetime),
            RequestWindow = TimeSpan.FromMinutes(window),
            MaximumRequests = maximumRequests
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
    }
}
