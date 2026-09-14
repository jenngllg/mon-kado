using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class TwoFactorServiceIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData("missingMember")]
    [InlineData("unenrolledMember")]
    [InlineData("pendingEnrollment")]
    public async Task GetStatusAsync_WhenAccountHasNoConfirmedFactor_ReturnsOnlyStatusOrRejectsMissingAccount(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        if (scenario == "pendingEnrollment")
        {
            database.MemberTwoFactors.Add(MemberTwoFactor.Create(memberId));
            await database.SaveChangesAsync(cancellationToken);
        }

        if (scenario == "missingMember")
            await database.Users.ExecuteDeleteAsync(cancellationToken);

        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        TwoFactorStatusResponse? status = null;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            status = await service.GetStatusAsync(
                memberId,
                cancellationToken);
        });

        // Assert
        if (scenario == "missingMember")
        {
            Assert.IsType<InvalidAuthenticationSessionException>(exception);
            Assert.Null(status);
        }
        else
        {
            Assert.Null(exception);
            Assert.NotNull(status);
            Assert.False(status.IsEnabled);
            Assert.Equal(
                0,
                status.RemainingRecoveryCodes);
        }
    }

    [Fact]
    public async Task GetSetupAsync_WhenProofWasNeverIssued_RejectsWithoutStagingSecurityMaterial()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.GetSetupAsync(
            cryptography.CreateFlow().Value,
            cancellationToken));

        // Assert
        Assert.IsType<TwoFactorAuthenticationFailedException>(exception);
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.TwoFactorChallenges.AnyAsync(cancellationToken));
        Assert.False(await database.MemberTwoFactors.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task ReauthenticateAsync_WhenAccountHasNoAuthenticator_RejectsManagementGrant()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.ReauthenticateAsync(
            memberId,
            Guid.CreateVersion7(),
            TwoFactorFlowPurpose.ReplaceAuthenticator,
            "123456",
            null,
            cancellationToken));

        // Assert
        Assert.IsType<TwoFactorAccessDeniedException>(exception);
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.TwoFactorChallenges.AnyAsync(cancellationToken));
    }
}
