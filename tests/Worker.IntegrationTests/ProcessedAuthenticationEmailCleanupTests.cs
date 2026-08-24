using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

[Collection(PostgreSqlWorkerTestSuite.Name)]
public class ProcessedAuthenticationEmailCleanupTests(PostgreSqlWorkerFixture fixture)
{
    private static readonly DateTime _cutoff = new(
        2026,
        7,
        25,
        10,
        0,
        0,
        DateTimeKind.Utc);

    [Fact]
    public async Task DeleteProcessedEmailsAsync_WhenCutoffReached_DeletesAllKindsButKeepsNewerAndPendingMessages()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await CreateProviderAsync(cancellationToken);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var (member, email) = CreateMember();
        var request = MemberEmailChangeRequest.Create(
            member.Id,
            email,
            "new-cleanup@example.test",
            "NEW-CLEANUP@EXAMPLE.TEST",
            _cutoff.AddDays(-2),
            _cutoff.AddDays(1));
        var eligibleMessages = new[]
        {
            AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
                member.Id,
                _cutoff.AddDays(-2)),
            AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
                request.Id,
                member.Id,
                request.NewEmail,
                "security-stamp",
                _cutoff.AddDays(-2)),
            AuthenticationEmailOutboxMessage.CreateEmailChangeSecurityNotification(
                request.Id,
                member.Id,
                request.CurrentEmail,
                _cutoff.AddDays(-2)),
            AuthenticationEmailOutboxMessage.CreatePasswordReset(
                member.Id,
                email,
                "security-stamp",
                _cutoff.AddDays(-2)),
            AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
                member.Id,
                email,
                _cutoff.AddDays(-2))
        };
        var newerMessage = AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
            member.Id,
            email,
            _cutoff.AddDays(-1));
        var pendingMessage = AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
            member.Id,
            email,
            _cutoff.AddDays(-40));
        context.Users.Add(member);
        context.MemberEmailChangeRequests.Add(request);
        context.AuthenticationEmailOutboxMessages.AddRange(eligibleMessages);
        context.AuthenticationEmailOutboxMessages.AddRange(
            newerMessage,
            pendingMessage);
        await context.SaveChangesAsync(cancellationToken);
        var eligibleIds = eligibleMessages
            .Select(message => message.Id)
            .ToArray();
        await context.AuthenticationEmailOutboxMessages
            .Where(message => eligibleIds.Contains(message.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    _cutoff),
                cancellationToken);
        await context.AuthenticationEmailOutboxMessages
            .Where(message => message.Id == newerMessage.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    _cutoff.AddMilliseconds(1)),
                cancellationToken);
        context.ChangeTracker.Clear();
        var cleanup = scope.ServiceProvider.GetRequiredService<IProcessedAuthenticationEmailCleanup>();

        // Act
        var deletedCount = await cleanup.DeleteProcessedEmailsAsync(
            _cutoff,
            500,
            cancellationToken);

        // Assert
        Assert.Equal(
            eligibleMessages.Length,
            deletedCount);
        var remainingMessages = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.Id)
            .ToArrayAsync(cancellationToken);
        Assert.Equal(
            2,
            remainingMessages.Length);
        Assert.Contains(
            remainingMessages,
            message => message.Id == newerMessage.Id && message.ProcessedAt is not null);
        Assert.Contains(
            remainingMessages,
            message => message.Id == pendingMessage.Id && message.ProcessedAt is null);
    }

    [Fact]
    public async Task DeleteProcessedEmailsAsync_WhenBatchIsBounded_DeletesOldestMessagesFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await CreateProviderAsync(cancellationToken);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var (member, email) = CreateMember();
        var messages = Enumerable.Range(
                0,
                3)
            .Select(index => AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
                member.Id,
                email,
                _cutoff
                    .AddDays(-10)
                    .AddMinutes(index)))
            .ToArray();
        context.Users.Add(member);
        context.AuthenticationEmailOutboxMessages.AddRange(messages);
        await context.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < messages.Length; index++)
        {
            var messageId = messages[index].Id;
            var processedAt = _cutoff
                .AddDays(-3)
                .AddMinutes(index);
            await context.AuthenticationEmailOutboxMessages
                .Where(message => message.Id == messageId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        message => message.ProcessedAt,
                        processedAt),
                    cancellationToken);
        }

        context.ChangeTracker.Clear();
        var cleanup = scope.ServiceProvider.GetRequiredService<IProcessedAuthenticationEmailCleanup>();

        // Act
        var firstDeletedCount = await cleanup.DeleteProcessedEmailsAsync(
            _cutoff,
            2,
            cancellationToken);
        var remainingAfterFirstBatch = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Select(message => message.Id)
            .SingleAsync(cancellationToken);
        var secondDeletedCount = await cleanup.DeleteProcessedEmailsAsync(
            _cutoff,
            2,
            cancellationToken);
        var thirdDeletedCount = await cleanup.DeleteProcessedEmailsAsync(
            _cutoff,
            2,
            cancellationToken);

        // Assert
        Assert.Equal(
            2,
            firstDeletedCount);
        Assert.Equal(
            messages[2].Id,
            remainingAfterFirstBatch);
        Assert.Equal(
            1,
            secondDeletedCount);
        Assert.Equal(
            0,
            thirdDeletedCount);
    }

    [Fact]
    public async Task DeleteProcessedEmailsAsync_WhenOldestMessageIsLocked_SkipsItWithoutWaiting()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await CreateProviderAsync(cancellationToken);
        await using var setupScope = provider.CreateAsyncScope();
        var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var (member, email) = CreateMember();
        var oldestMessage = AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
            member.Id,
            email,
            _cutoff.AddDays(-10));
        var nextMessage = AuthenticationEmailOutboxMessage.CreatePasswordChangedSecurityNotification(
            member.Id,
            email,
            _cutoff.AddDays(-9));
        setupContext.Users.Add(member);
        setupContext.AuthenticationEmailOutboxMessages.AddRange(
            oldestMessage,
            nextMessage);
        await setupContext.SaveChangesAsync(cancellationToken);
        await setupContext.AuthenticationEmailOutboxMessages
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    message => message.Id == oldestMessage.Id
                        ? _cutoff.AddDays(-2)
                        : _cutoff.AddDays(-1)),
                cancellationToken);
        await using var lockScope = provider.CreateAsyncScope();
        var lockContext = lockScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await using var transaction = await lockContext.Database.BeginTransactionAsync(cancellationToken);
        await lockContext.AuthenticationEmailOutboxMessages
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM public.authentication_email_outbox
                WHERE id = {oldestMessage.Id}
                FOR UPDATE
                """)
            .SingleAsync(cancellationToken);
        await using var cleanupScope = provider.CreateAsyncScope();
        var cleanup = cleanupScope.ServiceProvider
            .GetRequiredService<IProcessedAuthenticationEmailCleanup>();

        // Act
        var deletedCount = await cleanup.DeleteProcessedEmailsAsync(
            _cutoff,
            1,
            cancellationToken);

        // Assert
        Assert.Equal(
            1,
            deletedCount);
        Assert.True(await lockContext.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .AnyAsync(
                message => message.Id == oldestMessage.Id,
                cancellationToken));
        Assert.False(await lockContext.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .AnyAsync(
                message => message.Id == nextMessage.Id,
                cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DeleteProcessedEmailsAsync_WhenBatchSizeIsNotPositive_ThrowsArgumentOutOfRangeException(
        int batchSize)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var provider = await CreateProviderAsync(cancellationToken);
        await using var scope = provider.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IProcessedAuthenticationEmailCleanup>();

        // Act
        Task action() => cleanup.DeleteProcessedEmailsAsync(
            _cutoff,
            batchSize,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(action);
    }

    private async Task<ServiceProvider> CreateProviderAsync(CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();
        var services = new ServiceCollection();
        services.ConfigureInfrastructureInjection(configuration);
        var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.EnsureDeletedAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        return provider;
    }

    private static (MonKadoUser Member, string Email) CreateMember()
    {
        var id = Guid.CreateVersion7();
        var email = $"cleanup-{id:N}@example.test";

        var member = new MonKadoUser
        {
            Id = id,
            DisplayName = "Cleanup member",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true
        };

        return (
            member,
            email);
    }
}
