using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class AuthenticationAccessTokenIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("subject")]
    [InlineData("session")]
    [InlineData("member")]
    [InlineData("token")]
    public async Task ValidateAsync_WhenRegistryOrSessionIsInvalid_RejectsBearerIncludingOptionalEndpoints(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            cancellationToken);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization?.Parameter);
        var tokenId = Guid.Parse(jwt.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var metadata = await database.AuthenticationAccessTokens.SingleAsync(cancellationToken);
        var now = scope.ServiceProvider
            .GetRequiredService<TimeProvider>()
            .GetUtcNow()
            .UtcDateTime;
        switch (scenario)
        {
            case "unknown":
                await database.AuthenticationAccessTokens.ExecuteDeleteAsync(cancellationToken);
                break;
            case "revoked":
                await database.AuthenticationSessions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        session => session.RevokedAt,
                        now),
                    cancellationToken);
                break;
            case "expired":
                await database.AuthenticationSessions.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                        session => session.CreatedAt,
                        now.AddHours(-9))
                        .SetProperty(
                        session => session.RenewedAt,
                        now.AddHours(-9))
                        .SetProperty(
                        session => session.ExpiresAt,
                        now.AddHours(-1)),
                    cancellationToken);
                break;
            case "subject":
                var otherId = await ReportedWishlistTestData.CreateOwnerAsync(
                    factory,
                    cancellationToken);
                await database.AuthenticationSessions.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        session => session.UserId,
                        otherId),
                    cancellationToken);
                break;
            case "session":
                await database.AuthenticationSessions.ExecuteDeleteAsync(cancellationToken);
                break;
            case "member":
                await database.Users.ExecuteDeleteAsync(cancellationToken);
                break;
            case "token":
                await database.AuthenticationAccessTokens.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        token => token.ExpiresAt,
                        now.AddMinutes(-1)),
                    cancellationToken);
                break;
        }

        // Act
        var exception = await Record.ExceptionAsync(() => scope.ServiceProvider
            .GetRequiredService<IAuthenticatedMemberValidationService>()
            .ValidateAsync(
            memberId,
            tokenId,
            cancellationToken));
        using var protectedResponse = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        using var optionalResponse = await client.GetAsync(
            $"/api/v1/shared-wishlists/{Guid.CreateVersion7()}",
            cancellationToken);

        // Assert
        Assert.IsType<InvalidAccessTokenException>(exception);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            protectedResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            optionalResponse.StatusCode);
        Assert.False(protectedResponse.Headers.Contains("Set-Cookie"));
        Assert.False(optionalResponse.Headers.Contains("Set-Cookie"));
        var error = await optionalResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        Assert.Equal(
            401,
            error
                .GetProperty("statusCode")
                .GetInt32());
        Assert.NotEqual(
            Guid.Empty,
            metadata.SessionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteExpiredSessionsAsync_WhenMetadataAndAuditReachTheirDeadlines_CleansIndependentBatchesIdempotently(bool concurrent)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var now = new DateTime(
            2027,
            2,
            28,
            12,
            0,
            0,
            DateTimeKind.Utc);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            new FixedTimeProvider(new DateTimeOffset(now)));
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await database.AuthenticationSessions.SingleAsync(cancellationToken);
        var boundaryId = Guid.CreateVersion7();
        database.AuthenticationAccessTokens.Add(new AuthenticationAccessToken
        {
            Id = boundaryId,
            SessionId = session.Id,
            IssuedAt = now.AddMinutes(-16),
            ExpiresAt = now.AddSeconds(-30)
        });
        for (var index = 0; index < 3; index++)
        {
            database.AuthenticationAccessTokens.Add(new AuthenticationAccessToken
            {
                Id = Guid.CreateVersion7(),
                SessionId = session.Id,
                IssuedAt = now.AddHours(-1),
                ExpiresAt = now.AddSeconds(-31)
            });
            database.AdministrativeSessionRevocationEvents.Add(new AdministrativeSessionRevocationEvent
            {
                Id = Guid.CreateVersion7(),
                AdministratorId = administratorId,
                MemberId = memberId,
                RequestReference = "SUPPORT-809",
                CreatedAt = new DateTime(
                    2026,
                    8,
                    31,
                    12,
                    0,
                    0,
                    DateTimeKind.Utc)
            });
        }

        var retainedAuditId = Guid.CreateVersion7();
        database.AdministrativeSessionRevocationEvents.Add(new AdministrativeSessionRevocationEvent
        {
            Id = retainedAuditId,
            AdministratorId = administratorId,
            MemberId = memberId,
            RequestReference = "SUPPORT-809",
            CreatedAt = new DateTime(
                2026,
                8,
                31,
                12,
                0,
                1,
                DateTimeKind.Utc)
        });
        await database.SaveChangesAsync(cancellationToken);

        // Act
        var first = CleanAsync(
            factory,
            now,
            cancellationToken);
        var second = concurrent ? CleanAsync(
            factory,
            now,
            cancellationToken) : Task.FromResult(0);
        await Task.WhenAll(
            first,
            second);
        await CleanAsync(
            factory,
            now,
            cancellationToken);

        // Assert
        Assert.Equal(
            2,
            await database.AuthenticationAccessTokens.CountAsync(cancellationToken));
        Assert.True(await database.AuthenticationAccessTokens.AnyAsync(
            token => token.Id == boundaryId,
            cancellationToken));
        Assert.Equal(
            retainedAuditId,
            (await database.AdministrativeSessionRevocationEvents
                .AsNoTracking()
                .SingleAsync(cancellationToken)).Id);
        Assert.Single(await database.AuthenticationSessions.ToArrayAsync(cancellationToken));
        await CleanAsync(
            factory,
            now.AddSeconds(1),
            cancellationToken);
        Assert.Single(await database.AuthenticationAccessTokens.ToArrayAsync(cancellationToken));
        Assert.Empty(await database.AdministrativeSessionRevocationEvents.ToArrayAsync(cancellationToken));
    }

    [Fact]
    public async Task GetAuditAsync_WhenAccountsAreDeleted_RetainsAnonymousEventAndCascadesTokenMetadata()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var member = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<IAdministrativeSessionRevocationService>()
            .ExecuteAsync(
            administratorId,
            memberId,
            "SUPPORT-809",
            cancellationToken);
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Act
        await database.Users.ExecuteDeleteAsync(cancellationToken);
        var readerId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var reader = await AuthenticationTestData.CreateClientAsync(
            factory,
            readerId,
            cancellationToken);
        using var response = await reader.GetAsync(
            "/api/v1/admin/audit-events?action=memberSessionsRevoked",
            cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var entry = Assert.Single(body
            .GetProperty("items")
            .EnumerateArray());
        Assert.Equal(
            JsonValueKind.Null,
            entry
                .GetProperty("administratorId")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            entry
                .GetProperty("administratorDisplayName")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            entry
                .GetProperty("memberId")
                .ValueKind);
        Assert.Equal(
            1,
            await database.AuthenticationAccessTokens.CountAsync(cancellationToken));
    }

    private static async Task<int> CleanAsync(
        PostgreSqlApiFactory factory,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IExpiredAuthenticationSessionCleanup>()
            .DeleteExpiredSessionsAsync(
            cutoff,
            1,
            cancellationToken);
    }
}
