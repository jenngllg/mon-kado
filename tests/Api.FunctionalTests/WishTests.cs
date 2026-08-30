using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WishTests
{
    [Fact]
    public async Task CreateAsync_WhenRequestIsValid_ReturnsExactCreatedWishContractAndHeaders()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            new
            {
                name = "  Cafe\u0301  ",
                note = "  Édition blanche  ",
                url = "  https://example.com/gift  ",
                price = 12.34m
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishlistService.Accesses);
        var creation = Assert.Single(factory.WishService.Creations);
        Assert.Equal(
            ownerId,
            creation.OwnerId);
        Assert.Equal(
            wishlistId,
            creation.WishlistId);
        Assert.Equal(
            "Café",
            creation.Name);
        Assert.Equal(
            "Édition blanche",
            creation.Note);
        Assert.Equal(
            "https://example.com/gift",
            creation.Url);
        Assert.Equal(
            12.34m,
            creation.Price);
        Assert.Equal(
            1,
            creation.Quantity);
        Assert.Equal(
            $"/api/v1/wishlists/{wishlistId}/wishes/{creation.Id}",
            response.Headers.Location.AbsolutePath);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The created wish response is empty.");
        AssertWishContract(
            document.RootElement,
            creation.Id,
            wishlistId);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Creating wish {creation.Id} in wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wish {creation.Id} created in wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "Édition blanche",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenWishExists_ReturnsExactWishContractAndHeaders()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002a\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishlistService.Accesses);
        Assert.Equal(
            [(wishlistId, wishId)],
            factory.WishService.Retrievals);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The wish response is empty.");
        AssertWishContract(
            document.RootElement,
            wishId,
            wishlistId);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wish {wishId} retrieved from wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_WhenOptionalValuesAreOmitted_IncludesNullProperties()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes",
            new
            {
                name = "Cadeau"
            },
            TestContext.Current.CancellationToken);
        var wish = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);
        Assert.Equal(
            JsonValueKind.Null,
            wish.GetProperty("note").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            wish.GetProperty("url").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            wish.GetProperty("price").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            wish.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task CreateAsync_WhenBearerTokenIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes",
            new
            {
                name = "Cadeau"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.WishlistService.Accesses);
        Assert.Empty(factory.WishService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenWishlistIsNotOwned_ReturnsNotFoundBeforeCreation()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Access = WishlistAccess.NotOwned;
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            new
            {
                name = "Cadeau"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNotFound,
            error.ErrorCode);
        Assert.Empty(factory.WishService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenParentDisappearsAfterAuthorization_ReturnsNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishService.WishlistExists = false;
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes",
            new
            {
                name = "Cadeau"
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNotFound,
            error.ErrorCode);
        Assert.Single(factory.WishService.Creations);
    }

    [Fact]
    public async Task GetAsync_WhenWishDoesNotExistUnderParent_ReturnsWishNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{wishlistId}/wishes/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishNotFound,
            error.ErrorCode);
    }

    [Theory]
    [InlineData("{\"name\":null}", "name")]
    [InlineData("{\"name\":\"Cadeau\",\"url\":\"ftp://example.com/gift\"}", "url")]
    [InlineData("{\"name\":\"Cadeau\",\"price\":1.234}", "price")]
    public async Task CreateAsync_WhenRequestIsInvalid_ReturnsStructuredBadRequest(
        string json,
        string expectedPropertyName)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes",
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Contains(
            error.ValidationErrors ?? [],
            validation => validation.PropertyName == expectedPropertyName);
        Assert.Empty(factory.WishService.Creations);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequestIsValid_ReturnsExactUpdatedWishContractAndHeaders()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);

        // Act
        using var response = await PutAsync(
            client,
            wishlistId,
            wishId,
            new
            {
                name = "  Cafe\u0301 premium  ",
                note = "   ",
                url = "  https://example.com/premium  ",
                price = 24.68m,
                quantity = 4
            },
            "\"0000002a\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "\"0000002b\"",
            response.Headers.ETag?.Tag);
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishlistService.Accesses);
        var update = Assert.Single(factory.WishService.Updates);
        Assert.Equal(
            ownerId,
            update.OwnerId);
        Assert.Equal(
            wishlistId,
            update.WishlistId);
        Assert.Equal(
            wishId,
            update.WishId);
        Assert.Equal(
            "Café premium",
            update.Name);
        Assert.Null(update.Note);
        Assert.Equal(
            "https://example.com/premium",
            update.Url);
        Assert.Equal(
            24.68m,
            update.Price);
        Assert.Equal(
            4,
            update.Quantity);
        Assert.Equal(
            42u,
            update.ExpectedVersion);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The updated wish response is empty.");
        var wish = document.RootElement;
        Assert.Equal(
            wishId,
            wish.GetProperty("id").GetGuid());
        Assert.Equal(
            wishlistId,
            wish.GetProperty("wishlistId").GetGuid());
        Assert.Equal(
            "Café premium",
            wish.GetProperty("name").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            wish.GetProperty("note").ValueKind);
        Assert.Equal(
            "https://example.com/premium",
            wish.GetProperty("url").GetString());
        Assert.Equal(
            24.68m,
            wish.GetProperty("price").GetDecimal());
        Assert.Equal(
            4,
            wish.GetProperty("quantity").GetInt32());
        Assert.Equal(
            1,
            wish.GetProperty("position").GetInt64());
        Assert.Equal(
            "2026-08-25T13:00:00Z",
            wish.GetProperty("updatedAt").GetString());
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Updating wish {wishId} in wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wish {wishId} updated in wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            factory.LogMessages,
            message => message.Contains(
                "Café premium",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateAsync_WhenIfMatchIsMissing_ReturnsPreconditionRequired()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PutAsync(
            client,
            wishlistId,
            wishId,
            new
            {
                name = "Cadeau",
                quantity = 1
            },
            entityTag: null);

        // Assert
        Assert.Equal(
            HttpStatusCode.PreconditionRequired,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestPreconditionRequired,
            error.ErrorCode);
        Assert.Empty(factory.WishService.Updates);
    }

    [Fact]
    public async Task UpdateAsync_WhenVersionIsStale_ReturnsPreconditionFailed()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        factory.WishService.Exception = new WishVersionConflictException();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PutAsync(
            client,
            wishlistId,
            wishId,
            new
            {
                name = "Cadeau",
                quantity = 1
            },
            "\"0000002a\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.PreconditionFailed,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishVersionConflict,
            error.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenWishlistIsNotOwned_ReturnsWishlistNotFoundBeforeUpdate()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Access = WishlistAccess.NotOwned;
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PutAsync(
            client,
            wishlistId,
            Guid.CreateVersion7(),
            new
            {
                name = "Cadeau",
                quantity = 1
            },
            "\"0000002a\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNotFound,
            error.ErrorCode);
        Assert.Empty(factory.WishService.Updates);
    }

    [Fact]
    public async Task UpdateAsync_WhenWishDoesNotExistUnderParent_ReturnsWishNotFound()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PutAsync(
            client,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new
            {
                name = "Cadeau",
                quantity = 1
            },
            "\"0000002a\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishNotFound,
            error.ErrorCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequestIsInvalid_ReturnsStructuredBadRequest()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PutAsync(
            client,
            wishlistId,
            wishId,
            new
            {
                name = "   ",
                url = "ftp://example.com/gift",
                price = 0,
                quantity = 1
            },
            "\"0000002a\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            [
                "name",
                "price",
                "url"
            ],
            error.ValidationErrors?
                .Select(validation => validation.PropertyName)
                .OrderBy(propertyName => propertyName));
        Assert.Empty(factory.WishService.Updates);
    }

    [Fact]
    public async Task UpdateAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        factory.WishService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await PutAsync(
            client,
            wishlistId,
            wishId,
            new
            {
                name = "Cadeau",
                quantity = 1
            },
            "\"0000002a\"");

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var content = new StringContent(
            "name=Cadeau",
            Encoding.UTF8,
            "text/plain");
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes/{Guid.CreateVersion7()}")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            "\"0000002a\"");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Empty(factory.WishService.Updates);
    }

    [Fact]
    public async Task UpdateAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var content = new StringContent(
            "{\"name\":\"" + new string(
                'a',
                5 * 1024) + "\"}",
            Encoding.UTF8,
            "application/json");
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes/{Guid.CreateVersion7()}")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            "\"0000002a\"");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Empty(factory.WishService.Updates);
    }

    [Fact]
    public async Task DeleteAsync_WhenRequestIsValid_ReturnsNoContentAndHeaders()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = CreateDeleteRequest(
            wishlistId,
            wishId);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.ETag);
        Assert.Equal(
            string.Empty,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishlistService.Accesses);
        Assert.Equal(
            [(ownerId, wishlistId, wishId, 42u)],
            factory.WishService.Deletions);
        Assert.DoesNotContain(
            (wishlistId, wishId),
            factory.WishService.Wishes);
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Deleting wish {wishId} from wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
        Assert.Contains(
            factory.LogMessages,
            message => message.Contains(
                $"Wish {wishId} deleted from wishlist {wishlistId} for member {ownerId}",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_WhenIfMatchIsMissing_ReturnsPreconditionRequiredAfterAuthorization()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var ownerId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var client = CreateAuthorizedClient(
            factory,
            ownerId);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/wishes/{Guid.CreateVersion7()}");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            (HttpStatusCode)428,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestPreconditionRequired,
            error.ErrorCode);
        Assert.Equal(
            [(ownerId, wishlistId)],
            factory.WishlistService.Accesses);
        Assert.Empty(factory.WishService.Deletions);
    }

    [Fact]
    public async Task DeleteAsync_WhenIfMatchIsMalformed_ReturnsValidationError()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes/{Guid.CreateVersion7()}");
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            "invalid");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.RequestValidationError,
            error.ErrorCode);
        var validationError = Assert.Single(error.ValidationErrors ?? []);
        Assert.Equal(
            "ifMatch",
            validationError.PropertyName);
        Assert.Empty(factory.WishService.Deletions);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound, ErrorCodes.WishNotFound)]
    [InlineData("version", HttpStatusCode.PreconditionFailed, ErrorCodes.WishVersionConflict)]
    [InlineData("member", HttpStatusCode.Unauthorized, ErrorCodes.AccountAuthenticationSessionInvalid)]
    [InlineData("database", HttpStatusCode.ServiceUnavailable, ErrorCodes.TechnicalDependencyUnavailable)]
    public async Task DeleteAsync_WhenServiceRejectsDeletion_ReturnsStructuredError(
        string scenario,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        factory.WishService.Wishes[(wishlistId, wishId)] = CreateDetails(
            wishlistId,
            wishId);
        factory.WishService.WishlistExists = scenario != "missing";
        factory.WishService.Exception = scenario switch
        {
            "version" => new WishVersionConflictException(),
            "member" => new InvalidAuthenticationSessionException(),
            "database" => new DependencyUnavailableException(
                "PostgreSQL",
                new TimeoutException()),
            _ => null
        };
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = CreateDeleteRequest(
            wishlistId,
            wishId);

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedStatus,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            expectedErrorCode,
            error.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_WhenWishlistIsNotOwned_ReturnsWishlistNotFoundBeforePrecondition()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishlistService.Access = WishlistAccess.NotOwned;
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes/{Guid.CreateVersion7()}");

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.WishlistNotFound,
            error.ErrorCode);
        Assert.Empty(factory.WishService.Deletions);
    }

    [Fact]
    public async Task DeleteAsync_WhenBearerTokenIsMissing_ReturnsUnauthorized()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateDeleteRequest(
            Guid.CreateVersion7(),
            Guid.CreateVersion7());

        // Act
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Empty(factory.WishlistService.Accesses);
        Assert.Empty(factory.WishService.Deletions);
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlIsUnavailable_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        factory.WishService.Exception = new DependencyUnavailableException(
            "PostgreSQL",
            new TimeoutException());
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());

        // Act
        using var response = await client.GetAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(error);
        Assert.Equal(
            ErrorCodes.TechnicalDependencyUnavailable,
            error.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenContentTypeIsNotJson_ReturnsUnsupportedMediaType()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var content = new StringContent(
            "name=Cadeau",
            Encoding.UTF8,
            "text/plain");

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes",
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
        Assert.Empty(factory.WishService.Creations);
    }

    [Fact]
    public async Task CreateAsync_WhenBodyExceedsMaximumSize_ReturnsPayloadTooLarge()
    {
        // Arrange
        await using var factory = new RegistrationApiFactory();
        using var client = CreateAuthorizedClient(
            factory,
            Guid.CreateVersion7());
        using var content = new StringContent(
            "{\"name\":\"" + new string(
                'a',
                5 * 1024) + "\"}",
            Encoding.UTF8,
            "application/json");

        // Act
        using var response = await client.PostAsync(
            $"/api/v1/wishlists/{Guid.CreateVersion7()}/wishes",
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            response.StatusCode);
        Assert.Empty(factory.WishService.Creations);
    }

    private static HttpClient CreateAuthorizedClient(
        RegistrationApiFactory factory,
        Guid memberId)
    {
        var client = factory.CreateClient();
        var accessTokenService = factory.Services.GetRequiredService<IAccessTokenService>();
        var accessToken = accessTokenService.Create(memberId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            JwtBearerDefaults.AuthenticationScheme,
            accessToken.Value);

        return client;
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        Guid wishlistId,
        Guid wishId,
        object body,
        string? entityTag)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}")
        {
            Content = JsonContent.Create(body)
        };

        if (entityTag is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "If-Match",
                entityTag);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage CreateDeleteRequest(
        Guid wishlistId,
        Guid wishId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"/api/v1/wishlists/{wishlistId}/wishes/{wishId}");
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            "\"0000002a\"");

        return request;
    }

    private static WishDetails CreateDetails(
        Guid wishlistId,
        Guid wishId)
    {
        return new WishDetails(
            wishId,
            wishlistId,
            "Café",
            "Édition blanche",
            "https://example.com/gift",
            12.34m,
            1,
            new DateTime(
                2026,
                8,
                25,
                12,
                0,
                0,
                DateTimeKind.Utc),
            null,
            42);
    }

    private static void AssertWishContract(
        JsonElement wish,
        Guid wishId,
        Guid wishlistId)
    {
        Assert.Equal(
            [
                "id",
                "wishlistId",
                "name",
                "note",
                "url",
                "price",
                "quantity",
                "position",
                "createdAt",
                "updatedAt"
            ],
            wish
                .EnumerateObject()
                .Select(property => property.Name));
        Assert.Equal(
            wishId,
            wish.GetProperty("id").GetGuid());
        Assert.Equal(
            wishlistId,
            wish.GetProperty("wishlistId").GetGuid());
        Assert.Equal(
            "Café",
            wish.GetProperty("name").GetString());
        Assert.Equal(
            "Édition blanche",
            wish.GetProperty("note").GetString());
        Assert.Equal(
            "https://example.com/gift",
            wish.GetProperty("url").GetString());
        Assert.Equal(
            12.34m,
            wish.GetProperty("price").GetDecimal());
        Assert.Equal(
            1,
            wish.GetProperty("quantity").GetInt32());
        Assert.Equal(
            1,
            wish.GetProperty("position").GetInt64());
        Assert.Equal(
            "2026-08-25T12:00:00Z",
            wish.GetProperty("createdAt").GetString());
        Assert.Equal(
            JsonValueKind.Null,
            wish.GetProperty("updatedAt").ValueKind);
    }
}
