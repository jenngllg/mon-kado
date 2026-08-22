using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Interceptors;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class AuditableEntityInterceptorTests
{
    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        21,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void SavingChanges_WhenContextIsMissing_DoesNothing()
    {
        // Arrange
        var interceptor = new AuditableEntityInterceptor(new FixedTimeProvider(_now));
        var eventData = new DbContextEventData(
            null!,
            (_, _) => string.Empty,
            null);

        // Act
        var result = interceptor.SavingChanges(
            eventData,
            default);

        // Assert
        Assert.False(result.HasResult);
    }

    [Fact]
    public void SavingChanges_WhenEntityIsAdded_SetsAuditValues()
    {
        // Arrange
        using var context = CreateContext();
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_now),
            DisplayName = "Jenn"
        };
        context.Users.Add(user);
        var interceptor = new AuditableEntityInterceptor(new FixedTimeProvider(_now));
        var eventData = new DbContextEventData(
            null!,
            (_, _) => string.Empty,
            context);

        // Act
        var result = interceptor.SavingChanges(
            eventData,
            default);

        // Assert
        Assert.False(result.HasResult);
        Assert.Equal(
            _now.UtcDateTime,
            user.CreatedAt);
    }

    private static MonKadoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=mon_kado;Username=mon_kado;Password=test")
            .Options;

        return new MonKadoDbContext(options);
    }
}
