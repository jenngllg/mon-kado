using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Domain.UnitTests.Entities;

public class WishlistShareLinkTests
{
    [Fact]
    public void Rotate_WhenNewSecretIsProvided_ReplacesSecretMaterial()
    {
        // Arrange
        byte[] originalHash = [1];
        var shareLink = new WishlistShareLink(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            originalHash,
            "first");
        byte[] hash = [2];

        // Act
        shareLink.Rotate(
            hash,
            "second");

        // Assert
        Assert.Equal(
            hash,
            shareLink.SecretHash);
        Assert.NotSame(
            hash,
            shareLink.SecretHash);
        Assert.Equal(
            "second",
            shareLink.ProtectedSecret);
        Assert.NotSame(
            originalHash,
            shareLink.SecretHash);
    }
}
