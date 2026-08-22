using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class PostgreSqlFailureClassifierTests
{
    [Fact]
    public void IsUnavailable_WhenExceptionIsMissing_ThrowsArgumentNullException()
    {
        // Arrange
        // Act
        static void action() => _ = PostgreSqlFailureClassifier.IsUnavailable(null!);

        // Assert
        Assert.Throws<ArgumentNullException>((Action)action);
    }

    [Fact]
    public void IsUnavailable_WhenTimeoutIsNested_ReturnsTrue()
    {
        // Arrange
        var exception = new InvalidOperationException(
            "Outer",
            new TimeoutException());

        // Act
        var result = PostgreSqlFailureClassifier.IsUnavailable(exception);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUnavailable_WhenNpgsqlExceptionIsNotTransient_ReturnsFalse()
    {
        // Arrange
        var exception = new NpgsqlException("Permanent failure");

        // Act
        var result = PostgreSqlFailureClassifier.IsUnavailable(exception);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsUnavailable_WhenExceptionIsUnrelated_ReturnsFalse()
    {
        // Arrange
        var exception = new InvalidOperationException();

        // Act
        var result = PostgreSqlFailureClassifier.IsUnavailable(exception);

        // Assert
        Assert.False(result);
    }
}
