using JennGllg.Fr.MonKado.Back.Api.ModelBinding;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

using System.Globalization;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.ModelBinding;

public class NonBlankOptionalQueryModelBinderTests
{
    private readonly NonBlankOptionalQueryModelBinder<int?> _binder = new(NullLoggerFactory.Instance);

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, true)]
    [InlineData(" ", false, true)]
    [InlineData("7", true, false)]
    [InlineData("bad", false, true)]
    public async Task BindModelAsync_WhenQueryIsOptional_DistinguishesAbsenceFromInvalidValues(
        string? input,
        bool modelSet,
        bool invalid)
    {
        // Arrange
        var values = new Dictionary<string, StringValues>();

        if (input is not null)
            values.Add(
                "page",
                input);
        var context = new DefaultModelBindingContext
        {
            ActionContext = new ActionContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestAborted = TestContext.Current.CancellationToken
                }
            },
            ModelMetadata = new EmptyModelMetadataProvider()
                .GetMetadataForType(typeof(int?)),
            ModelName = "page",
            ModelState = new ModelStateDictionary(),
            ValueProvider = new QueryStringValueProvider(
                BindingSource.Query,
                new QueryCollection(values),
                CultureInfo.InvariantCulture)
        };

        // Act
        await _binder.BindModelAsync(context);

        // Assert
        Assert.Equal(
            modelSet,
            context.Result.IsModelSet);
        Assert.Equal(
            invalid,
            context.ModelState.ErrorCount > 0);

        if (modelSet)
            Assert.Equal(
                7,
                context.Result.Model);

        if (input is "" or " ")
        {
            var entry = context.ModelState["page"];
            Assert.NotNull(entry);
            Assert.Contains(
                entry.Errors,
                error => error.ErrorMessage == ValidationMessages.BlankQueryParameter);
        }
    }
}
