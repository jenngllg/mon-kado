using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class WishlistShareLinkUrlServiceTests
{
    [Fact]
    public void Build_WhenValuesAreValid_ReturnsFrontendFragmentUrl()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new WishlistSharingOptions
        {
            FrontendOrigin = "https://app.example.test"
        });
        var service = new WishlistShareLinkUrlService(options);
        var id = Guid.Parse("0198e75d-8280-7000-8000-000000000001");

        // Act
        var url = service.Build(
            id,
            "secret");

        // Assert
        Assert.Equal(
            "https://app.example.test/#/shared-wishlists/0198e75d828070008000000000000001.secret",
            url);
    }

    [Fact]
    public void Constructor_WhenFrontendOriginIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = Microsoft.Extensions.Options.Options.Create(new WishlistSharingOptions());

        // Act
        var action = () => new WishlistShareLinkUrlService(options);

        // Assert
        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains(
            "FrontendOrigin",
            exception.Message,
            StringComparison.Ordinal);
    }
}
