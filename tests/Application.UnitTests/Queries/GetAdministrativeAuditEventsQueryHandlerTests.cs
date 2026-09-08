using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetAdministrativeAuditEventsQueryHandlerTests
{
    private readonly Mock<IAdministrativeAuditService> _serviceMock;
    private readonly GetAdministrativeAuditEventsQueryHandler _handler;

    public GetAdministrativeAuditEventsQueryHandlerTests()
    {
        _serviceMock = new Mock<IAdministrativeAuditService>(MockBehavior.Strict);
        _handler = new GetAdministrativeAuditEventsQueryHandler(
            _serviceMock.Object,
            NullLogger<GetAdministrativeAuditEventsQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenValidated_NormalizesFiltersAndForwardsCancellation(bool filters)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetAdministrativeAuditEventsQuery
        {
            CallerId = Guid.CreateVersion7(),
            Action = filters ? AdministrativeAuditAction.MemberErased : null,
            AdministratorId = filters ? Guid.CreateVersion7() : null,
            MemberId = filters ? Guid.CreateVersion7() : null,
            WishlistId = filters ? Guid.CreateVersion7() : null,
            ExportId = filters ? Guid.CreateVersion7() : null,
            RequestReference = filters ? "  SUPPORT-806  " : null,
            From = filters ? "2026-09-01T14:00:00+02:00" : null,
            To = filters ? "2026-09-01T15:00:00+02:00" : null,
            Page = filters ? 2 : null,
            PageSize = filters ? 7 : null
        };
        var expected = TestFixture
            .Create()
            .Create<AdministrativeAuditPage>();
        AdministrativeAuditFilter? captured = null;
        _serviceMock
            .Setup(service => service.GetPageAsync(
                It.IsAny<AdministrativeAuditFilter>(),
                cancellationToken))
            .Callback<AdministrativeAuditFilter, CancellationToken>((
                filter,
                _) => captured = filter)
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        Assert.NotNull(captured);
        Assert.Equal(
            query.Action,
            captured.Action);
        Assert.Equal(
            query.AdministratorId,
            captured.AdministratorId);
        Assert.Equal(
            query.MemberId,
            captured.MemberId);
        Assert.Equal(
            query.WishlistId,
            captured.WishlistId);
        Assert.Equal(
            query.ExportId,
            captured.ExportId);
        Assert.Equal(
            filters ? "SUPPORT-806" : null,
            captured.RequestReference);
        Assert.Equal(
            filters ? 2 : 1,
            captured.Page);
        Assert.Equal(
            filters ? 7 : 20,
            captured.PageSize);
        Assert.Equal(
            filters ? new DateTime(
                2026,
                9,
                1,
                12,
                0,
                0,
                DateTimeKind.Utc) : (DateTime?)null,
            captured.From);
        Assert.Equal(
            filters ? new DateTime(
                2026,
                9,
                1,
                13,
                0,
                0,
                DateTimeKind.Utc) : (DateTime?)null,
            captured.To);
        _serviceMock.Verify(
            service => service.GetPageAsync(
                captured,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
