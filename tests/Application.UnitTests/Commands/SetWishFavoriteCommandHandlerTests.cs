using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class SetWishFavoriteCommandHandlerTests
{
    private readonly Mock<IWishService> _wishServiceMock;
    private readonly SetWishFavoriteCommandHandler _handler;

    public SetWishFavoriteCommandHandlerTests()
    {
        _wishServiceMock = new Mock<IWishService>(MockBehavior.Strict);
        _handler = new SetWishFavoriteCommandHandler(
            _wishServiceMock.Object,
            NullLogger<SetWishFavoriteCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_WhenServiceCompletes_TransmitsVersionAndCancellationToken(
        bool preference,
        bool missing)
    {
        // Arrange
        var command = new SetWishFavoriteCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            preference,
            42);
        var cancellationToken = TestContext.Current.CancellationToken;
        var details = new WishDetails(
            command.WishId,
            command.WishlistId,
            "Wish",
            null,
            null,
            null,
            1,
            DateTime.UnixEpoch,
            null,
            43,
            isFavorite: preference);
        _wishServiceMock
            .Setup(service => service.SetFavoriteAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                preference,
                command.ExpectedVersion,
                cancellationToken))
            .ReturnsAsync(missing ? null : details);

        // Act
        var action = () => _handler.Handle(
            command,
            cancellationToken);

        // Assert

        if (missing)
            await Assert.ThrowsAsync<WishNotFoundException>(action);

        if (!missing)
            Assert.Same(
                details,
                await action());

        _wishServiceMock.Verify(
            service => service.SetFavoriteAsync(
                command.OwnerId,
                command.WishlistId,
                command.WishId,
                preference,
                command.ExpectedVersion,
                cancellationToken),
            Times.Once);
        _wishServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public void CreateValidationException_WhenPipelineRejectsRequest_PreservesErrors()
    {
        // Arrange
        IGenericValidationFailure command = new SetWishFavoriteCommand(
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            null,
            0);
        var errors = new[]
        {
            new ValidationError(
                "isFavorite",
                "Required")
        };

        // Act
        var exception = command.CreateValidationException(errors);

        // Assert
        Assert.IsType<RequestValidationException>(exception);
        _wishServiceMock.VerifyNoOtherCalls();
    }
}
