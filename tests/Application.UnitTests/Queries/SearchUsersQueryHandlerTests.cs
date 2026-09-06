using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class SearchUsersQueryHandlerTests
{
    private readonly Mock<IUserSearchService> _serviceMock;
    private readonly SearchUsersQueryHandler _handler;
    public SearchUsersQueryHandlerTests()
    {
        _serviceMock = new Mock<IUserSearchService>(MockBehavior.Strict);
        _handler = new SearchUsersQueryHandler(
            _serviceMock.Object,
            NullLogger<SearchUsersQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(null, null, null, "", 1, 20)]
    [InlineData("  E\u0301lodie  ", 2, 10, "Élodie", 2, 10)]
    public async Task Handle_WhenCalled_NormalizesAndForwardsPaginationAndCancellation(
        string? term,
        int? page,
        int? pageSize,
        string normalized,
        int expectedPage,
        int expectedPageSize)
    {
        // Arrange
        var fixture = TestFixture.Create();
        var expected = fixture.Create<UserSearchPage>();
        var cancellationToken = TestContext.Current.CancellationToken;
        _serviceMock
            .Setup(service => service.SearchAsync(
                normalized,
                expectedPage,
                expectedPageSize,
                cancellationToken))
            .ReturnsAsync(expected);
        var query = new SearchUsersQuery(
            term,
            page,
            pageSize);

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _serviceMock.Verify(
            service => service.SearchAsync(
                normalized,
                expectedPage,
                expectedPageSize,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
