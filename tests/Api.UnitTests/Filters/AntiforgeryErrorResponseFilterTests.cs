using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Filters;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Filters;

public class AntiforgeryErrorResponseFilterTests
{
    private readonly AntiforgeryErrorResponseFilter _filter = new(NullLogger<AntiforgeryErrorResponseFilter>.Instance);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnResultExecutionAsync_WhenResultIsEvaluated_OnlyNormalizesAntiforgery(bool antiforgery)
    {
        // Arrange
        var action = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor());
        var original = antiforgery ? (IActionResult)new AntiforgeryValidationFailedResult() : new BadRequestResult();
        var context = new ResultExecutingContext(
            action,
            [],
            original,
            new object());
        var calls = 0;

        // Act
        await _filter.OnResultExecutionAsync(
            context,
            () =>
            {
                calls++;

                return Task.FromResult(new ResultExecutedContext(
                    action,
                    [],
                    context.Result,
                    context.Controller));
            });

        // Assert
        Assert.Equal(
            1,
            calls);

        if (!antiforgery)
        {
            Assert.Same(
                original,
                context.Result);
            Assert.False(context.HttpContext.Response.Headers.ContainsKey("Cache-Control"));

            return;
        }
        var result = Assert.IsType<ObjectResult>(context.Result);
        var error = Assert.IsType<ErrorResponse>(result.Value);
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            result.StatusCode);
        Assert.Equal(
            ErrorCodes.SecurityCsrfValidationFailed,
            error.ErrorCode);
        Assert.Equal(
            "no-store",
            context.HttpContext.Response.Headers.CacheControl.ToString());
    }
}
