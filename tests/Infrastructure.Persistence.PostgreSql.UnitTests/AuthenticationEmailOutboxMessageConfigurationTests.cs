using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class AuthenticationEmailOutboxMessageConfigurationTests
{
    [Fact]
    public void ConvertKindToDatabase_WhenKindIsKnown_ReturnsDatabaseValue()
    {
        // Arrange

        // Act
        var value = AuthenticationEmailOutboxMessageConfiguration.ConvertKindToDatabase(
            AuthenticationEmailKind.EmailConfirmation);

        // Assert
        Assert.Equal(
            "EMAIL_CONFIRMATION",
            value);
    }

    [Fact]
    public void ConvertKindToDatabase_WhenKindIsUnknown_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var kind = (AuthenticationEmailKind)int.MaxValue;

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            AuthenticationEmailOutboxMessageConfiguration.ConvertKindToDatabase(kind));

        // Assert
        Assert.Equal(
            "kind",
            exception.ParamName);
    }

    [Fact]
    public void ConvertKindFromDatabase_WhenValueIsKnown_ReturnsKind()
    {
        // Arrange

        // Act
        var kind = AuthenticationEmailOutboxMessageConfiguration.ConvertKindFromDatabase(
            "EMAIL_CONFIRMATION");

        // Assert
        Assert.Equal(
            AuthenticationEmailKind.EmailConfirmation,
            kind);
    }

    [Fact]
    public void ConvertKindFromDatabase_WhenValueIsUnknown_ThrowsInvalidOperationException()
    {
        // Arrange

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AuthenticationEmailOutboxMessageConfiguration.ConvertKindFromDatabase("UNKNOWN"));

        // Assert
        Assert.Equal(
            "Unknown authentication email kind 'UNKNOWN'.",
            exception.Message);
    }
}
