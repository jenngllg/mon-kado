using JennGllg.Fr.MonKado.Back.Api.Transformers;

using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Transformers;

public class PersonalDataExportOperationTransformerTests
{
    private readonly PersonalDataExportOperationTransformer _transformer = new();
    [Theory]
    [InlineData(null, false)]
    [InlineData("api/v1/members/current/data-exports", true)]
    [InlineData("api/v1/members/current/data-exports", false)]
    public async Task TransformAsync_WhenMetadataIsAbsentOrHeadersAreNew_HandlesGeneratedOperations(
        string? path,
        bool missingResponses)
    {
        // Arrange
        await using var services = new ServiceCollection().BuildServiceProvider();
        var context = new OpenApiOperationTransformerContext
        {
            ApplicationServices = services,
            DocumentName = "v1",
            Description = new ApiDescription
            {
                RelativePath = path,
                HttpMethod = "POST"
            }
        };
        var operation = new OpenApiOperation
        {
            Responses = missingResponses ? null : new OpenApiResponses
            {
                ["200"] = new OpenApiResponse(),
                ["202"] = new OpenApiResponse()
            }
        };

        // Act
        await _transformer.TransformAsync(
            operation,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        if (path is not null && !missingResponses)
        {
            Assert.NotNull(operation.Responses);
            Assert.True(operation.Responses["200"].Headers?.ContainsKey("Location"));
            Assert.True(operation.Responses["202"].Headers?.ContainsKey("Location"));
        }
        else
            if (!missingResponses)
                Assert.Null(operation.Responses?["200"].Headers);
            else
                Assert.Null(operation.Responses);
    }
}
