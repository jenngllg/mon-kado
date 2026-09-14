using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class TwoFactorUnavailableTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenPostgreSqlIsUnavailable_ReportsDependencyFailureWithoutReturningMaterial(bool readStatus)
    {
        // Arrange
        await using var factory = new UnavailablePostgreSqlApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {

            if (readStatus)
                await service.GetStatusAsync(
                    Guid.CreateVersion7(),
                    cancellationToken);
            else
                await service.GetSetupAsync(
                    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                    cancellationToken);
        });

        // Assert
        Assert.IsType<DependencyUnavailableException>(exception);
    }
}
