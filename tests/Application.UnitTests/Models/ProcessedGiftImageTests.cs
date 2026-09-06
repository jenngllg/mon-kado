using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Models;

public class ProcessedGiftImageTests
{
    [Fact]
    public void Constructor_WhenSourceBuffersAreMutated_PreservesProcessedImage()
    {
        // Arrange
        var content = new byte[]
        {
            1,
            2
        };
        var contentHash = new byte[]
        {
            3,
            4
        };
        var processedImage = new ProcessedGiftImage(
            content,
            contentHash);

        // Act
        content[0] = 9;
        contentHash[0] = 9;
        var returnedHash = processedImage.ContentHash;
        returnedHash[0] = 9;

        // Assert
        Assert.Equal(
            new byte[]
            {
                1,
                2
            },
            processedImage.Content.ToArray());
        Assert.Equal(
            new byte[]
            {
                3,
                4
            },
            processedImage.ContentHash);
    }
}
