using AutoFixture;

using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests.Configurations;

[Collection(PostgreSqlApiTestSuite.Name)]
public class InfrastructureInjectionConfigurationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData(16, 16)]
    [InlineData(96, 32)]
    public async Task ConfigureInfrastructureInjection_WhenBurstCompletes_BoundsRetainedContextsWithoutRejectingScopes(
        int concurrentScopes,
        int expectedRetainedContexts)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var firstContexts = new HashSet<Guid>();
        var scopes = new List<AsyncServiceScope>();

        try
        {
            for (var index = 0; index < concurrentScopes; index++)
            {
                var scope = factory.Services.CreateAsyncScope();
                scopes.Add(scope);
                var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
                Assert.Same(
                    context,
                    scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
                await context.Users.CountAsync(cancellationToken);
                firstContexts.Add(context.ContextId.InstanceId);
            }
        }
        finally
        {
            foreach (var scope in scopes)
                await scope.DisposeAsync();
            scopes.Clear();
        }

        // Act
        var reusedContexts = 0;

        try
        {
            for (var index = 0; index < concurrentScopes; index++)
            {
                var scope = factory.Services.CreateAsyncScope();
                scopes.Add(scope);
                var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
                await context.Users.CountAsync(cancellationToken);

                if (firstContexts.Contains(context.ContextId.InstanceId))
                    reusedContexts++;
            }
        }
        finally
        {
            foreach (var scope in scopes)
                await scope.DisposeAsync();
        }

        // Assert
        Assert.Equal(
            concurrentScopes,
            firstContexts.Count);
        Assert.Equal(
            expectedRetainedContexts,
            reusedContexts);
    }

    [Fact]
    public async Task ConfigureInfrastructureInjection_WhenContextIsReused_DiscardsUnsavedTrackedChanges()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        using var client = factory.CreateClient();
        var csrf = await client.GetFromJsonAsync<CsrfTokenResponse>(
            "/security/csrf-token",
            cancellationToken);
        Assert.NotNull(csrf);
        client.DefaultRequestHeaders.Add(
            "X-CSRF-TOKEN",
            csrf.Token);
        var request = TestFixture.Create()
            .Build<RegisterAccountRequest>()
            .FromFactory(() => new RegisterAccountRequest(
                "context-retention@example.test",
                "SyntheticPoolIntegration-2026!",
                "Original display name"))
            .Create();
        using var registration = await client.PostAsJsonAsync(
            "/api/v1/auth/registrations",
            request,
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.Accepted,
            registration.StatusCode);
        Guid originalContextId;
        string? originalEmail;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            originalContextId = context.ContextId.InstanceId;
            var user = await context.Users.SingleAsync(cancellationToken);
            originalEmail = user.Email;
            user.Email = "unsaved-change@example.test";
        }

        // Act
        await using var nextScope = factory.Services.CreateAsyncScope();
        var reusedContext = nextScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var hasTrackedChanges = reusedContext.ChangeTracker.HasChanges();
        var storedUser = await reusedContext.Users.SingleAsync(cancellationToken);

        // Assert
        Assert.Equal(
            originalContextId,
            reusedContext.ContextId.InstanceId);
        Assert.False(hasTrackedChanges);
        Assert.Equal(
            originalEmail,
            storedUser.Email);
    }
}
