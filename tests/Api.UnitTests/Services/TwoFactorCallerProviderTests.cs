using JennGllg.Fr.MonKado.Back.Api.Services;

using Microsoft.AspNetCore.Http;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class TwoFactorCallerProviderTests
{
    private readonly HttpContextAccessor _accessor = new();
    private readonly TwoFactorCallerProvider _provider;

    public TwoFactorCallerProviderTests()
    {
        _provider = new TwoFactorCallerProvider(_accessor);
    }

    [Theory]
    [InlineData("noContext")]
    [InlineData("noIdentity")]
    [InlineData("emptyPrincipal")]
    [InlineData("anonymous")]
    [InlineData("invalidSubject")]
    [InlineData("invalidToken")]
    [InlineData("valid")]
    public void GetCurrent_WhenPrincipalIsInspected_ReturnsOnlyAuthenticatedTechnicalIdentifiers(string scenario)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var tokenId = Guid.CreateVersion7();

        if (scenario != "noContext")
        {
            _accessor.HttpContext = new DefaultHttpContext();

            if (scenario == "emptyPrincipal")
                _accessor.HttpContext.User = new ClaimsPrincipal();

            if (scenario is not ("noIdentity" or "emptyPrincipal"))
                _accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(
                            JwtRegisteredClaimNames.Sub,
                            scenario == "invalidSubject" ? "invalid" : memberId.ToString("D")),
                        new Claim(
                            JwtRegisteredClaimNames.Jti,
                            scenario == "invalidToken" ? "invalid" : tokenId.ToString("D"))
                    ],
                    scenario == "anonymous" ? null : "Bearer"));
        }

        // Act
        var caller = _provider.GetCurrent();

        // Assert

        if (scenario == "valid")
        {
            Assert.NotNull(caller);
            Assert.Equal(
                memberId,
                caller.MemberId);
            Assert.Equal(
                tokenId,
                caller.AccessTokenId);

            return;
        }

        Assert.Null(caller);
    }
}
