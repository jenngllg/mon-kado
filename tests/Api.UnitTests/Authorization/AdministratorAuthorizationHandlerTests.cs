using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using Moq;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Authorization;

public class AdministratorAuthorizationHandlerTests
{
    private readonly Mock<IAdministratorAccessService> _accessServiceMock;
    private readonly AdministratorAuthorizationHandler _handler;
    private readonly HttpContextAccessor _httpContextAccessor;
    private readonly AdministratorRequirement _requirement = new();
    public AdministratorAuthorizationHandlerTests()
    {
        _accessServiceMock = new Mock<IAdministratorAccessService>(MockBehavior.Strict);
        _httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                RequestAborted = TestContext.Current.CancellationToken
            }
        };
        _handler = new AdministratorAuthorizationHandler(
            _accessServiceMock.Object,
            _httpContextAccessor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleAsync_WhenDatabaseGrantsAdministratorAccess_SucceedsWithoutJwtRole(bool hasHttpContext)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();

        if (!hasHttpContext)
            _httpContextAccessor.HttpContext = null;
        var cancellationToken = hasHttpContext ? TestContext.Current.CancellationToken : CancellationToken.None;
        var context = CreateContext(memberId.ToString());
        _accessServiceMock
            .Setup(service => service.GetAccessAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(AdministratorAccess.Granted);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
        _accessServiceMock.Verify(
            service => service.GetAccessAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _accessServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AdministratorAccess.Forbidden, typeof(AdministratorAccessDeniedException))]
    [InlineData(AdministratorAccess.MemberNotFound, typeof(InvalidAuthenticationSessionException))]
    public async Task HandleAsync_WhenDatabaseDeniesAccess_DoesNotTrustJwtRole(
        AdministratorAccess access,
        Type expectedException)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var context = CreateContext(memberId.ToString());
        var identity = Assert.IsType<ClaimsIdentity>(context.User.Identity);
        identity.AddClaim(new Claim(
                ClaimTypes.Role,
                "Admin"));
        _accessServiceMock
            .Setup(service => service.GetAccessAsync(
                memberId,
                TestContext.Current.CancellationToken))
            .ReturnsAsync(access);

        // Act
        var exception = await Record.ExceptionAsync(() => _handler.HandleAsync(context));

        // Assert
        Assert.IsType(
            expectedException,
            exception);
        Assert.False(context.HasSucceeded);
        _accessServiceMock.Verify(
            service => service.GetAccessAsync(
                memberId,
                TestContext.Current.CancellationToken),
            Times.Once);
        _accessServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task HandleAsync_WhenSubjectIsInvalid_RejectsBeforeDatabaseAccess(string? subject)
    {
        // Arrange
        var context = CreateContext(subject);

        // Act
        var action = () => _handler.HandleAsync(context);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        Assert.False(context.HasSucceeded);
        _accessServiceMock.VerifyNoOtherCalls();
    }

    private AuthorizationHandlerContext CreateContext(string? subject)
    {
        var identity = new ClaimsIdentity();

        if (subject is not null)
            identity.AddClaim(new Claim(
                    JwtRegisteredClaimNames.Sub,
                    subject));

        return new AuthorizationHandlerContext(
            [_requirement],
            new ClaimsPrincipal(identity),
            null);
    }
}
