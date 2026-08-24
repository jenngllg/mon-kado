using JennGllg.Fr.MonKado.Back.Api.Errors;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Errors;

public class ApiStatusCodeResponseWriterTests
{
    [Fact]
    public async Task WriteAsync_WhenCancellationTokenIsCanceled_CancelsResponseWrite()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Body = new MemoryStream();
        using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellationTokenSource.Cancel();

        // Act
        var action = () => ApiStatusCodeResponseWriter.WriteAsync(
            context,
            cancellationTokenSource.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
    }
}
