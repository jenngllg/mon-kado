using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Options;

public class ProductCatalogOptionsValidatorTests
{
    [Theory]
    [InlineData(false, "", true)]
    [InlineData(true, "key_public_index123", true)]
    [InlineData(true, "", false)]
    [InlineData(true, "private_admin_key", false)]
    [InlineData(true, "key_", false)]
    [InlineData(true, "key_index&extra=x", false)]
    public void Validate_WhenPublicIndexIsConfigured_RejectsInvalidIdentifiers(
        bool enabled,
        string key,
        bool expected)
    {
        // Arrange
        var validator = new ProductCatalogOptionsValidator();
        var options = new ProductCatalogOptions { Enabled = enabled, IndexKey = key };

        // Act
        var result = validator.Validate(
            null,
            options);

        // Assert
        Assert.Equal(
            expected,
            result.Succeeded);
    }

    [Fact]
    public void Validate_WhenIndexIsTooLong_RejectsConfiguration()
    {
        // Arrange
        var validator = new ProductCatalogOptionsValidator();
        var options = new ProductCatalogOptions { Enabled = true, IndexKey = "key_" + new string('x', 97) };

        // Act
        var result = validator.Validate(
            null,
            options);

        // Assert
        Assert.True(result.Failed);
    }
}
