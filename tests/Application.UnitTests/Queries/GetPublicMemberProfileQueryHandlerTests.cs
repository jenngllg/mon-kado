using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetPublicMemberProfileQueryHandlerTests
{
    private readonly Mock<IPublicMemberProfileService> _serviceMock;
    private readonly GetPublicMemberProfileQueryHandler _handler;

    public GetPublicMemberProfileQueryHandlerTests()
    {
        _serviceMock = new Mock<IPublicMemberProfileService>(MockBehavior.Strict);
        _handler = new GetPublicMemberProfileQueryHandler(
            _serviceMock.Object,
            NullLogger<GetPublicMemberProfileQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_WhenServiceCompletes_ReturnsProfileOrNotFound(bool found)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = PublicMemberProfileTestData.CreateWithSharedList();
        var id = expected.Id;
        _serviceMock
            .Setup(service => service.GetAsync(
                id,
                cancellationToken))
            .ReturnsAsync(found ? expected : null);
        var query = new GetPublicMemberProfileQuery(id);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        if (found)
            Assert.Same(
                expected,
                await action());
        else
            await Assert.ThrowsAsync<PublicMemberProfileNotFoundException>(action);
        _serviceMock.Verify(
            service => service.GetAsync(
                id,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
