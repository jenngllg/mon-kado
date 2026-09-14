using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class TwoFactorInputValidationTests
{
    [Theory]
    [InlineData(
        null,
        false)]
    [InlineData(
        "",
        false)]
    [InlineData(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        true)]
    [InlineData(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB",
        false)]
    [InlineData(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        false)]
    [InlineData(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n",
        false)]
    public void IsFlow_WhenEncodingIsProvided_RequiresCanonicalUnpaddedProof(
        string? flow,
        bool expected)
    {
        // Arrange
        // Act
        var actual = TwoFactorInputValidation.IsFlow(flow);

        // Assert
        Assert.Equal(
            expected,
            actual);
    }

    [Theory]
    [InlineData(
        null,
        false)]
    [InlineData(
        "000123",
        true)]
    [InlineData(
        "12345",
        false)]
    [InlineData(
        "1234567",
        false)]
    [InlineData(
        "123456\n",
        false)]
    [InlineData(
        " 123456",
        false)]
    [InlineData(
        "１２３４５６",
        false)]
    public void IsCode_WhenCodeIsProvided_RequiresExactlySixAsciiDigits(
        string? code,
        bool expected)
    {
        // Arrange
        // Act
        var actual = TwoFactorInputValidation.IsCode(code);

        // Assert
        Assert.Equal(
            expected,
            actual);
    }

    [Theory]
    [InlineData(
        null,
        false)]
    [InlineData(
        "",
        false)]
    [InlineData(
        "0123456789abcdef0123456789ABCDEF",
        true)]
    [InlineData(
        "01234567-89abcdef-01234567-89ABCDEF",
        true)]
    [InlineData(
        "01234567-89ab-cdef-0123-456789ABCDEF",
        false)]
    [InlineData(
        "0123456789abcdef0123456789ABCDEG",
        false)]
    [InlineData(
        "0123456789abcdef0123456789ABCDEF\n",
        false)]
    public void IsRecoveryCode_WhenCodeIsProvided_RequiresExactHexadecimalEncoding(
        string? code,
        bool expected)
    {
        // Arrange
        // Act
        var actual = TwoFactorInputValidation.IsRecoveryCode(code);

        // Assert
        Assert.Equal(
            expected,
            actual);
    }
}
