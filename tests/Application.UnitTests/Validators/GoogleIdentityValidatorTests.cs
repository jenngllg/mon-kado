using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GoogleIdentityValidatorTests
{
    private readonly GoogleIdentityValidator _validator;

    public GoogleIdentityValidatorTests()
    {
        _validator = new GoogleIdentityValidator();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public async Task ValidateAsync_WhenSubjectLengthIsSupported_ReturnsValid(int length)
    {
        // Arrange
        var identity = CreateIdentity(new string(
            'A',
            length));

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("subject with spaces")]
    [InlineData("sübject")]
    public async Task ValidateAsync_WhenSubjectIsInvalid_ReturnsSubjectFailure(string subject)
    {
        // Arrange
        var identity = CreateIdentity(subject);

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GoogleIdentity.Subject));
    }

    [Fact]
    public async Task ValidateAsync_WhenSubjectIsTooLong_ReturnsSubjectFailure()
    {
        // Arrange
        var identity = CreateIdentity(new string(
            'A',
            256));

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GoogleIdentity.Subject));
    }

    [Fact]
    public async Task ValidateAsync_WhenEmailIsNotVerified_ReturnsEmailVerifiedFailure()
    {
        // Arrange
        var identity = new GoogleIdentity(
            "subject",
            "member@gmail.com",
            false,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GoogleIdentity.EmailVerified));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public async Task ValidateAsync_WhenEmailIsInvalid_ReturnsEmailFailure(string? email)
    {
        // Arrange
        var identity = new GoogleIdentity(
            "subject",
            email,
            true,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GoogleIdentity.Email));
    }

    [Fact]
    public async Task ValidateAsync_WhenOptionalClaimsAreValid_ReturnsValid()
    {
        // Arrange
        var identity = new GoogleIdentity(
            "subject",
            "member@company.example",
            true,
            "company.example",
            "Member name");

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenHostedDomainIsTooLong_ReturnsHostedDomainFailure()
    {
        // Arrange
        var identity = new GoogleIdentity(
            "subject",
            "member@company.example",
            true,
            new string(
                'a',
                254),
            null);

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GoogleIdentity.HostedDomain));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("Member\u0001")]
    public async Task ValidateAsync_WhenDisplayNameIsInvalid_ReturnsDisplayNameFailure(
        string displayName)
    {
        // Arrange
        var identity = new GoogleIdentity(
            "subject",
            "member@gmail.com",
            true,
            null,
            displayName);

        // Act
        var result = await _validator.ValidateAsync(
            identity,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GoogleIdentity.DisplayName));
    }

    private static GoogleIdentity CreateIdentity(string subject)
    {

        return new GoogleIdentity(
            subject,
            "member@gmail.com",
            true,
            null,
            null);
    }
}
