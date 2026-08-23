using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class InvariantFallbackLookupNormalizerTests
{
    private readonly RecordingLookupNormalizer _innerNormalizer;
    private readonly InvariantFallbackLookupNormalizer _normalizer;

    public InvariantFallbackLookupNormalizerTests()
    {
        _innerNormalizer = new RecordingLookupNormalizer();
        _normalizer = new InvariantFallbackLookupNormalizer(_innerNormalizer);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("member@example.fr", "NORMALIZED-EMAIL", "NORMALIZED-EMAIL")]
    [InlineData("member@example.fr", null, "MEMBER@EXAMPLE.FR")]
    public void NormalizeEmail_WhenCalled_ReturnsExpectedValue(
        string? email,
        string? innerResult,
        string? expectedResult)
    {
        // Arrange
        _innerNormalizer.EmailResult = innerResult;

        // Act
        var result = _normalizer.NormalizeEmail(email);

        // Assert
        Assert.Equal(
            expectedResult,
            result);
        Assert.Equal(
            email is null
                ? 0
                : 1,
            _innerNormalizer.EmailCallCount);
        Assert.Equal(
            email,
            _innerNormalizer.LastEmail);
        Assert.Equal(
            0,
            _innerNormalizer.NameCallCount);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("member name", "NORMALIZED-NAME", "NORMALIZED-NAME")]
    [InlineData("member name", null, "MEMBER NAME")]
    public void NormalizeName_WhenCalled_ReturnsExpectedValue(
        string? name,
        string? innerResult,
        string? expectedResult)
    {
        // Arrange
        _innerNormalizer.NameResult = innerResult;

        // Act
        var result = _normalizer.NormalizeName(name);

        // Assert
        Assert.Equal(
            expectedResult,
            result);
        Assert.Equal(
            name is null
                ? 0
                : 1,
            _innerNormalizer.NameCallCount);
        Assert.Equal(
            name,
            _innerNormalizer.LastName);
        Assert.Equal(
            0,
            _innerNormalizer.EmailCallCount);
    }
}
