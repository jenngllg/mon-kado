using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CreateWishCommandValidatorTests
{
    private readonly CreateWishCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenCommandIsComplete_ReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand(
            "Console 🎁",
            "Édition blanche\nAvec une manette",
            "https://example.com/console",
            499.99m);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenOptionalValuesAreNull_ReturnsSuccess()
    {
        // Arrange
        var command = CreateCommand(
            "Cadeau",
            null,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(true, false, nameof(CreateWishCommand.OwnerId))]
    [InlineData(false, true, nameof(CreateWishCommand.WishlistId))]
    public async Task ValidateAsync_WhenIdentifierIsEmpty_ReturnsIdentifierFailure(
        bool ownerIsEmpty,
        bool wishlistIsEmpty,
        string propertyName)
    {
        // Arrange
        var command = new CreateWishCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            "Cadeau",
            null,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == propertyName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Cadeau\ninterdit")]
    public async Task ValidateAsync_WhenNameIsInvalid_ReturnsSingleNameFailure(string? name)
    {
        // Arrange
        var command = CreateCommand(
            name,
            null,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(
            result.Errors,
            error => error.PropertyName == nameof(command.Name));
    }

    [Fact]
    public async Task ValidateAsync_WhenNoteIsInvalid_ReturnsNoteFailure()
    {
        // Arrange
        var command = CreateCommand(
            "Cadeau",
            "Note\u0001",
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Note));
    }

    [Fact]
    public async Task ValidateAsync_WhenUrlIsInvalid_ReturnsUrlFailure()
    {
        // Arrange
        var command = CreateCommand(
            "Cadeau",
            null,
            "ftp://example.com/gift",
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Url));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.234")]
    [InlineData("123456789.12")]
    public async Task ValidateAsync_WhenPriceIsInvalid_ReturnsPriceFailure(string price)
    {
        // Arrange
        var command = CreateCommand(
            "Cadeau",
            null,
            null,
            decimal.Parse(
                price,
                System.Globalization.CultureInfo.InvariantCulture));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Price));
    }

    private static CreateWishCommand CreateCommand(
        string? name,
        string? note,
        string? url,
        decimal? price)
    {
        return new CreateWishCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            name,
            note,
            url,
            price);
    }
}
