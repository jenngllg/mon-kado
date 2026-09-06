using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Options;

public class UrlImportOptionsValidatorTests
{
    [Theory]
    [InlineData(20, 2097152, 3, 10, true)]
    [InlineData(1, 1, 0, 1, true)]
    [InlineData(0, 2097152, 3, 10, false)]
    [InlineData(21, 2097152, 3, 10, false)]
    [InlineData(20, 0, 3, 10, false)]
    [InlineData(20, 2097153, 3, 10, false)]
    [InlineData(20, 2097152, -1, 10, false)]
    [InlineData(20, 2097152, 4, 10, false)]
    [InlineData(20, 2097152, 3, 0, false)]
    [InlineData(20, 2097152, 3, 11, false)]
    public void Validate_WhenLimitsAreChecked_RejectsUnsafeConfiguration(
        int timeout,
        int htmlBytes,
        int redirects,
        int permitLimit,
        bool expected)
    {
        // Arrange
        var validator = new UrlImportOptionsValidator();
        var options = new UrlImportOptions
        {
            TimeoutSeconds = timeout,
            MaximumHtmlBytes = htmlBytes,
            MaximumRedirects = redirects,
            PermitLimit = permitLimit
        };

        // Act
        var result = validator.Validate(
            null,
            options);

        // Assert
        Assert.Equal(
            expected,
            result.Succeeded);
    }
}
