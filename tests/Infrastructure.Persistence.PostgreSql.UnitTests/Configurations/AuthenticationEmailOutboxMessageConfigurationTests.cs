using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class AuthenticationEmailOutboxMessageConfigurationTests
{
    [Theory]
    [InlineData(AuthenticationEmailKind.EmailConfirmation, "EMAIL_CONFIRMATION")]
    [InlineData(AuthenticationEmailKind.EmailChangeConfirmation, "EMAIL_CHANGE_CONFIRMATION")]
    [InlineData(
        AuthenticationEmailKind.EmailChangeSecurityNotification,
        "EMAIL_CHANGE_SECURITY_NOTIFICATION")]
    public void Convert_WhenKnownKindIsProvided_UsesDatabaseValue(
        AuthenticationEmailKind kind,
        string expectedDatabaseValue)
    {
        // Arrange
        using var context = CreateContext();
        var converter = GetKindConverter(context);

        // Act
        var databaseValue = converter.ConvertToProvider(
            kind);
        var modelValue = converter.ConvertFromProvider(expectedDatabaseValue);

        // Assert
        Assert.Equal(
            expectedDatabaseValue,
            databaseValue);
        Assert.Equal(
            kind,
            modelValue);
    }

    [Fact]
    public void ConvertToProvider_WhenKindIsUnknown_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        using var context = CreateContext();
        var converter = GetKindConverter(context);

        // Act
        object? action() => converter.ConvertToProvider((AuthenticationEmailKind)int.MaxValue);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>((Func<object?>)action);
    }

    [Fact]
    public void ConvertFromProvider_WhenDatabaseValueIsUnknown_ThrowsInvalidOperationException()
    {
        // Arrange
        using var context = CreateContext();
        var converter = GetKindConverter(context);

        // Act
        object? action() => converter.ConvertFromProvider("UNKNOWN");

        // Assert
        Assert.Throws<InvalidOperationException>((Func<object?>)action);
    }

    private static MonKadoDbContext CreateContext()
    {
        return new MonKadoDbContext(
            new DbContextOptionsBuilder<MonKadoDbContext>()
                .UseNpgsql("Host=localhost;Database=mon_kado;Username=mon_kado;Password=test")
                .Options);
    }

    private static Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter GetKindConverter(
        MonKadoDbContext context)
    {
        var entityType = context.Model.FindEntityType(typeof(AuthenticationEmailOutboxMessage));
        Assert.NotNull(entityType);
        var property = entityType.FindProperty(nameof(AuthenticationEmailOutboxMessage.Kind));
        Assert.NotNull(property);
        var converter = property.GetValueConverter();
        Assert.NotNull(converter);

        return converter;
    }
}
