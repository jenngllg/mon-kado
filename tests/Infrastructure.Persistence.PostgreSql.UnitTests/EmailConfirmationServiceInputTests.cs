using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class EmailConfirmationServiceInputTests
{
    [Theory]
    [InlineData("invalid", "token")]
    [InlineData("00000000-0000-0000-0000-000000000000", "token")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "A")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "!!!")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "_w")]
    public async Task ConfirmAsync_WhenInputCannotBeDecoded_ReturnsFalse(
        string userId,
        string token)
    {
        // Arrange
        using var context = CreateContext();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var normalizer = new StubLookupNormalizer(null);
        var userRepositoryMock = new Mock<IMonKadoUserRepository>(MockBehavior.Strict);
        var outboxRepositoryMock =
            new Mock<IAuthenticationEmailOutboxRepository>(MockBehavior.Strict);
        var storeMock = new Mock<IUserStore<MonKadoUser>>(MockBehavior.Strict);
        var service = CreateService(
            context,
            provider,
            normalizer,
            userRepositoryMock,
            outboxRepositoryMock,
            storeMock);

        // Act
        var confirmed = await service.ConfirmAsync(
            userId,
            token,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(confirmed);
        userRepositoryMock.VerifyNoOtherCalls();
        outboxRepositoryMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestAsync_WhenNormalizedEmailIsBlank_CompletesWithoutPersistence(
        string? normalizedEmail)
    {
        // Arrange
        using var context = CreateContext();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var normalizer = new StubLookupNormalizer(normalizedEmail);
        var userRepositoryMock = new Mock<IMonKadoUserRepository>(MockBehavior.Strict);
        var outboxRepositoryMock =
            new Mock<IAuthenticationEmailOutboxRepository>(MockBehavior.Strict);
        var storeMock = new Mock<IUserStore<MonKadoUser>>(MockBehavior.Strict);
        var service = CreateService(
            context,
            provider,
            normalizer,
            userRepositoryMock,
            outboxRepositoryMock,
            storeMock);

        // Act
        await service.RequestAsync(
            "user@example.com",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            normalizer.NormalizeEmailCallCount);
        userRepositoryMock.VerifyNoOtherCalls();
        outboxRepositoryMock.VerifyNoOtherCalls();
        storeMock.VerifyNoOtherCalls();
    }

    private static EmailConfirmationService CreateService(
        MonKadoDbContext context,
        IServiceProvider serviceProvider,
        StubLookupNormalizer normalizer,
        Mock<IMonKadoUserRepository>? userRepositoryMock = null,
        Mock<IAuthenticationEmailOutboxRepository>? outboxRepositoryMock = null,
        Mock<IUserStore<MonKadoUser>>? storeMock = null)
    {
        userRepositoryMock ??= new(MockBehavior.Strict);
        outboxRepositoryMock ??= new(MockBehavior.Strict);
        storeMock ??= new(MockBehavior.Strict);
        var userManager = new UserManager<MonKadoUser>(
            storeMock.Object,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<MonKadoUser>(),
            [],
            [],
            normalizer,
            new IdentityErrorDescriber(),
            serviceProvider,
            NullLogger<UserManager<MonKadoUser>>.Instance);

        return new(
            context,
            context,
            userRepositoryMock.Object,
            outboxRepositoryMock.Object,
            userManager,
            normalizer,
            TimeProvider.System);
    }

    private static MonKadoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=mon_kado;Username=mon_kado;Password=test")
            .Options;

        return new MonKadoDbContext(options);
    }
}
