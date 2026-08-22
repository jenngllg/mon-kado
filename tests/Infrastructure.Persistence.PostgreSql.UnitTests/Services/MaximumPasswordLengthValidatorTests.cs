using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class MaximumPasswordLengthValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("password")]
    public async Task ValidateAsync_WhenPasswordDoesNotExceedLimit_ReturnsSuccess(string? password)
    {
        // Arrange
        var storeMock = new Mock<IUserStore<MonKadoUser>>(MockBehavior.Strict);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var userManager = CreateUserManager(
            storeMock.Object,
            provider);
        var validator = new MaximumPasswordLengthValidator<MonKadoUser>();
        var user = new MonKadoUser();

        // Act
        var result = await validator.ValidateAsync(
            userManager,
            user,
            password);

        // Assert
        Assert.True(result.Succeeded);
        storeMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidateAsync_WhenPasswordExceedsLimit_ReturnsFailure()
    {
        // Arrange
        var storeMock = new Mock<IUserStore<MonKadoUser>>(MockBehavior.Strict);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var userManager = CreateUserManager(
            storeMock.Object,
            provider);
        var validator = new MaximumPasswordLengthValidator<MonKadoUser>();
        var user = new MonKadoUser();
        var password = new string(
            'a',
            129);

        // Act
        var result = await validator.ValidateAsync(
            userManager,
            user,
            password);

        // Assert
        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            "PasswordTooLong",
            error.Code);
        storeMock.VerifyNoOtherCalls();
    }

    private static UserManager<MonKadoUser> CreateUserManager(
        IUserStore<MonKadoUser> store,
        IServiceProvider serviceProvider)
    {
        return new UserManager<MonKadoUser>(
            store,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<MonKadoUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            serviceProvider,
            NullLogger<UserManager<MonKadoUser>>.Instance);
    }
}
