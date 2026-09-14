using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class UnauthenticatedTwoFactorCallerProviderTests
{
    [Fact]
    public void GetCurrent_WhenNoHttpRequestExists_GrantsNoIdentity()
    {
        // Arrange
        var provider = new UnauthenticatedTwoFactorCallerProvider();

        // Act
        var caller = provider.GetCurrent();

        // Assert
        Assert.Null(caller);
    }
}
