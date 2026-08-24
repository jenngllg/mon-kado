using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CreateWishlistCommandValidatorTests
{
    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        24,
        12,
        0,
        0,
        TimeSpan.Zero);

    private readonly CreateWishlistCommandValidator _validator;
    private readonly Mock<TimeProvider> _timeProviderMock;

    public CreateWishlistCommandValidatorTests()
    {
        _timeProviderMock = new Mock<TimeProvider>(MockBehavior.Strict);
        _timeProviderMock
            .Setup(provider => provider.GetUtcNow())
            .Returns(_now);
        _validator = new CreateWishlistCommandValidator(_timeProviderMock.Object);
    }

    [Theory]
    [InlineData(WishlistOccasion.Birthday)]
    [InlineData(WishlistOccasion.Christmas)]
    [InlineData(WishlistOccasion.Wedding)]
    [InlineData(WishlistOccasion.Birth)]
    [InlineData(WishlistOccasion.Other)]
    public async Task ValidateAsync_WhenCommandIsValid_ReturnsSuccess(WishlistOccasion occasion)
    {
        // Arrange
        var command = CreateCommand(
            "Liste 🎁",
            occasion,
            new DateOnly(
                2026,
                8,
                24),
            "Première ligne\nDeuxième ligne\tfin");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        _timeProviderMock.Verify(
            provider => provider.GetUtcNow(),
            Times.Once);
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenOptionalValuesAreNull_ReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            WishlistOccasion.Other,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenOwnerIdIsEmpty_ReturnsOwnerFailure()
    {
        // Arrange
        var command = new CreateWishlistCommand(
            Guid.Empty,
            "Liste",
            WishlistOccasion.Other,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.OwnerId));
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Liste\nanniversaire")]
    [InlineData("Liste\u2028anniversaire")]
    [InlineData("Liste\u2029anniversaire")]
    [InlineData("Liste\u0001anniversaire")]
    public async Task ValidateAsync_WhenNameIsInvalid_ReturnsNameFailure(string? name)
    {
        // Arrange
        var command = CreateCommand(
            name,
            WishlistOccasion.Other,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        var nameErrors = result.Errors
            .Where(error => error.PropertyName == nameof(command.Name))
            .ToArray();
        Assert.Single(nameErrors);
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenNameExceedsMaximumLength_ReturnsNameFailure()
    {
        // Arrange
        var command = CreateCommand(
            new string(
                'a',
                WishlistTextValidation.MaximumNameLength + 1),
            WishlistOccasion.Other,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Name));
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenNameContainsUnpairedSurrogate_ReturnsNameFailure()
    {
        // Arrange
        var command = CreateCommand(
            new string(
                (char)0xD800,
                1),
            WishlistOccasion.Other,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Name));
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    public async Task ValidateAsync_WhenOccasionIsInvalid_ReturnsOccasionFailure(int? occasion)
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            (WishlistOccasion?)occasion,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        var occasionErrors = result.Errors
            .Where(error => error.PropertyName == nameof(command.Occasion))
            .ToArray();
        Assert.Single(occasionErrors);
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenEventDateIsPast_ReturnsEventDateFailure()
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            WishlistOccasion.Other,
            new DateOnly(
                2026,
                8,
                23),
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.EventDate));
        _timeProviderMock.Verify(
            provider => provider.GetUtcNow(),
            Times.Once);
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenEventDateIsFuture_ReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            WishlistOccasion.Other,
            new DateOnly(
                2026,
                8,
                25),
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        _timeProviderMock.Verify(
            provider => provider.GetUtcNow(),
            Times.Once);
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenMessageExceedsMaximumLength_ReturnsMessageFailure()
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            WishlistOccasion.Other,
            null,
            new string(
                'a',
                WishlistTextValidation.MaximumMessageLength + 1));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Message));
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenMessageContainsUnsupportedControlCharacter_ReturnsMessageFailure()
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            WishlistOccasion.Other,
            null,
            "Message\u0001");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Message));
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Text")]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\t")]
    public async Task ValidateAsync_WhenMessageCharactersAreSupported_ReturnsSuccess(string message)
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            WishlistOccasion.Other,
            null,
            message);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        _timeProviderMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenMessageContainsUnpairedSurrogate_ReturnsMessageFailure()
    {
        // Arrange
        var command = CreateCommand(
            "Liste",
            WishlistOccasion.Other,
            null,
            new string(
                (char)0xD800,
                1));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Message));
        _timeProviderMock.VerifyNoOtherCalls();
    }

    private static CreateWishlistCommand CreateCommand(
        string? name,
        WishlistOccasion? occasion,
        DateOnly? eventDate,
        string? message)
    {
        return new CreateWishlistCommand(
            Guid.CreateVersion7(),
            name,
            occasion,
            eventDate,
            message);
    }
}
