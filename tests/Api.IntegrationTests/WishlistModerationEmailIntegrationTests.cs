using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class WishlistModerationEmailIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTimeOffset _referenceTime = new(
        2026,
        9,
        7,
        12,
        0,
        0,
        TimeSpan.Zero);
    [Fact]
    public async Task DispatchAsync_WhenEarlierDecisionFails_PreservesOrderAndUsesCurrentRecipient()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new FixedTimeProvider(_referenceTime);
        var sender = new FakeWishlistModerationEmailSender();
        await using var factory = CreateFactory(
            clock,
            sender);
        var wishlistId = await SeedDecisionsAsync(
            factory,
            cancellationToken);
        var failed = false;
        sender.BeforeSendAsync = (
            message,
            token) =>
        {
            token.ThrowIfCancellationRequested();

            if (!failed)
            {
                failed = true;

                throw new WishlistModerationEmailDeliveryException(
                    WishlistModerationEmailFailure.Transient,
                    null);
            }

            return Task.CompletedTask;
        };
        var policy = CreatePolicy();

        // Act
        var firstCount = await DispatchAsync(
            factory,
            policy,
            cancellationToken);
        var waitingCount = await DispatchAsync(
            factory,
            policy,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => context.Wishlists.Any(wishlist => wishlist.Id == wishlistId && wishlist.OwnerId == member.Id))
            .ExecuteUpdateAsync(
            setters => setters.SetProperty(
                member => member.Email,
                "current-owner@example.test"),
            cancellationToken);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var retriedCount = await DispatchAsync(
            factory,
            policy,
            cancellationToken);
        var messages = sender.Messages.ToArray();
        var outbox = await context.WishlistModerationEmails
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        clock.UtcNow = clock.UtcNow.AddDays(31);
        await DispatchAsync(
            factory,
            policy,
            cancellationToken);
        var retainedOutboxCount = await context.WishlistModerationEmails.CountAsync(cancellationToken);
        var historyCount = await context.WishlistModerationEvents.CountAsync(cancellationToken);

        // Assert
        Assert.Equal(
            1,
            firstCount);
        Assert.Equal(
            0,
            waitingCount);
        Assert.Equal(
            2,
            retriedCount);
        Assert.Equal(
            new[] {
                WishlistModerationAction.Suspended,
                WishlistModerationAction.Reactivated
            },
            messages.Select(message => message.Action));
        Assert.All(
            messages,
            message => Assert.Equal(
                "current-owner@example.test",
                message.RecipientAddress));
        Assert.All(
            outbox,
            message => Assert.NotNull(message.ProcessedAt));
        Assert.Equal(
            0,
            retainedOutboxCount);
        Assert.Equal(
            2,
            historyCount);
    }

    [Fact]
    public async Task ClaimAsync_WhenLeaseIsReclaimed_RejectsStaleAcknowledgementAndBlocksLaterDecisions()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new FixedTimeProvider(_referenceTime);
        await using var factory = CreateFactory(
            clock,
            new FakeWishlistModerationEmailSender());
        await SeedDecisionsAsync(
            factory,
            cancellationToken);
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IWishlistModerationEmailRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<IWishlistModerationEmailRepository>();
        var now = clock
            .GetUtcNow()
            .UtcDateTime;
        var firstClaim = await firstRepository.ClaimAsync(
            now,
            TimeSpan.FromMinutes(2),
            cancellationToken);
        Assert.NotNull(firstClaim);

        // Act
        var concurrentClaim = await secondRepository.ClaimAsync(
            now,
            TimeSpan.FromMinutes(2),
            cancellationToken);
        now = now.AddMinutes(3);
        var newClaim = await secondRepository.ClaimAsync(
            now,
            TimeSpan.FromMinutes(2),
            cancellationToken);
        Assert.NotNull(newClaim);
        await firstRepository.CompleteAsync(
            firstClaim,
            now,
            true,
            now,
            null,
            cancellationToken);
        var newMessage = await secondRepository.GetMessageAsync(
            newClaim,
            now,
            cancellationToken);
        await secondRepository.CompleteAsync(
            newClaim,
            now,
            true,
            now,
            null,
            cancellationToken);
        var followingClaim = await firstRepository.ClaimAsync(
            now,
            TimeSpan.FromMinutes(2),
            cancellationToken);

        // Assert
        Assert.Null(concurrentClaim);
        Assert.Equal(
            firstClaim.EventId,
            newClaim.EventId);
        Assert.NotEqual(
            firstClaim.LeaseId,
            newClaim.LeaseId);
        Assert.NotNull(newMessage);
        Assert.NotNull(followingClaim);
        Assert.NotEqual(
            firstClaim.EventId,
            followingClaim.EventId);
    }

    [Fact]
    public async Task DispatchAsync_WhenOwnerIsNoLongerConfirmed_DoesNotSend()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var sender = new FakeWishlistModerationEmailSender();
        await using var factory = CreateFactory(
            new FixedTimeProvider(_referenceTime),
            sender);
        var wishlistId = await SeedDecisionsAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => context.Wishlists.Any(wishlist => wishlist.Id == wishlistId && wishlist.OwnerId == member.Id))
            .ExecuteUpdateAsync(
            setters => setters.SetProperty(
                member => member.EmailConfirmed,
                false),
            cancellationToken);

        // Act
        await DispatchAsync(
            factory,
            CreatePolicy(),
            cancellationToken);

        // Assert
        Assert.Empty(sender.Messages);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ClaimAsync_WhenWorkersCompete_ClaimsOneDecisionPerWishlist(int wishlistCount)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = CreateFactory(
            new FixedTimeProvider(_referenceTime),
            new FakeWishlistModerationEmailSender());
        for (var index = 0; index < wishlistCount; index++)
            await SeedDecisionsAsync(
                factory,
                cancellationToken);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workers = Enumerable
            .Range(
            0,
            8)
            .Select(async _ =>
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IWishlistModerationEmailRepository>();
                await start.Task.WaitAsync(cancellationToken);

                return await repository.ClaimAsync(
                    _referenceTime.UtcDateTime,
                    TimeSpan.FromMinutes(2),
                    cancellationToken);
            })
            .ToArray();

        // Act
        start.TrySetResult();
        var claims = await Task.WhenAll(workers);
        var ownedClaims = claims
            .OfType<WishlistModerationEmailClaim>()
            .ToArray();
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationRepository = verificationScope.ServiceProvider.GetRequiredService<IWishlistModerationEmailRepository>();

        // Assert
        Assert.Equal(
            wishlistCount,
            ownedClaims.Length);
        Assert.Equal(
            wishlistCount,
            ownedClaims
                .Select(claim => claim.EventId)
                .Distinct()
                .Count());
        foreach (var claim in ownedClaims)
        {
            var message = await verificationRepository.GetMessageAsync(
                claim,
                _referenceTime.UtcDateTime,
                cancellationToken);
            Assert.NotNull(message);
            Assert.Equal(
                WishlistModerationAction.Suspended,
                message.Action);
            Assert.Equal(
                1,
                claim.AttemptCount);
        }
    }

    [Fact]
    public async Task DispatchAsync_WhenMaximumAttemptsFail_StopsRetryingAndUnblocksLaterDecision()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new FixedTimeProvider(_referenceTime);
        var sender = new FakeWishlistModerationEmailSender();
        await using var factory = CreateFactory(
            clock,
            sender);
        await SeedDecisionsAsync(
            factory,
            cancellationToken);
        var attempts = 0;
        sender.BeforeSendAsync = (
            message,
            token) =>
        {
            token.ThrowIfCancellationRequested();

            if (message.Action is WishlistModerationAction.Suspended)
            {
                attempts++;

                throw new WishlistModerationEmailDeliveryException(
                    WishlistModerationEmailFailure.Transient,
                    null);
            }

            return Task.CompletedTask;
        };
        var policy = CreatePolicy();

        // Act
        for (var index = 0; index < 10; index++)
        {
            await DispatchAsync(
                factory,
                policy,
                cancellationToken);
            clock.UtcNow = clock.UtcNow.AddHours(1);
        }

        var remaining = await DispatchAsync(
            factory,
            policy,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var exhausted = await context.WishlistModerationEmails
            .AsNoTracking()
            .SingleAsync(
            message => message.LastError == "Transient",
            cancellationToken);

        // Assert
        Assert.Equal(
            10,
            attempts);
        Assert.Equal(
            10,
            exhausted.AttemptCount);
        Assert.NotNull(exhausted.ProcessedAt);
        Assert.Equal(
            0,
            remaining);
        Assert.Equal(
            WishlistModerationAction.Reactivated,
            Assert
                .Single(sender.Messages)
                .Action);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DispatchAsync_WhenResourceIsDeleted_CascadesPendingEmailsAndAudit(bool deleteOwner)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var sender = new FakeWishlistModerationEmailSender();
        await using var factory = CreateFactory(
            new FixedTimeProvider(_referenceTime),
            sender);
        var wishlistId = await SeedDecisionsAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        // Act
        if (deleteOwner)
            await context.Users
                .Where(member => context.Wishlists.Any(wishlist => wishlist.Id == wishlistId && wishlist.OwnerId == member.Id))
                .ExecuteDeleteAsync(cancellationToken);
        else
            await context.Wishlists
                .Where(wishlist => wishlist.Id == wishlistId)
                .ExecuteDeleteAsync(cancellationToken);
        var processed = await DispatchAsync(
            factory,
            CreatePolicy(),
            cancellationToken);
        var auditCount = await context.WishlistModerationEvents.CountAsync(cancellationToken);
        var outboxCount = await context.WishlistModerationEmails.CountAsync(cancellationToken);

        // Assert
        Assert.Equal(
            0,
            processed);
        Assert.Equal(
            0,
            auditCount);
        Assert.Equal(
            0,
            outboxCount);
        Assert.Empty(sender.Messages);
    }

    private PostgreSqlApiFactory CreateFactory(
        TimeProvider clock,
        FakeWishlistModerationEmailSender sender)
    {

        return new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock,
            configureServices: services =>
            {
                services.AddSingleton<IWishlistModerationEmailSender>(sender);
                services.ConfigureWishlistModerationEmailDelivery();
            });
    }

    private static WishlistModerationEmailDeliveryPolicy CreatePolicy()
    {

        return new WishlistModerationEmailDeliveryPolicy(
            20,
            TimeSpan.FromMinutes(2),
            10,
            [
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5)
            ],
            TimeSpan.FromHours(24),
            TimeSpan.FromDays(30));
    }

    private static async Task<int> DispatchAsync(
        PostgreSqlApiFactory factory,
        WishlistModerationEmailDeliveryPolicy policy,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWishlistModerationEmailDispatcher>();

        return await dispatcher.DispatchAsync(
            policy,
            cancellationToken);
    }

    private static async Task<Guid> SeedDecisionsAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var ownerId = Guid.CreateVersion7();
        var administratorId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var ownerEmail = $"{ownerId:N}@example.test";
        var administratorEmail = $"{administratorId:N}@example.test";
        context.Users.AddRange(
            new MonKadoUser
            {
                Id = ownerId,
                Email = ownerEmail,
                EmailConfirmed = true,
                UserName = ownerEmail,
                NormalizedUserName = ownerEmail.ToUpperInvariant(),
                NormalizedEmail = ownerEmail.ToUpperInvariant(),
                SecurityStamp = Guid
                    .CreateVersion7()
                    .ToString(),
                DisplayName = "Owner"
            },
            new MonKadoUser
            {
                Id = administratorId,
                Email = administratorEmail,
                EmailConfirmed = true,
                UserName = administratorEmail,
                NormalizedUserName = administratorEmail.ToUpperInvariant(),
                NormalizedEmail = administratorEmail.ToUpperInvariant(),
                SecurityStamp = Guid
                    .CreateVersion7()
                    .ToString(),
                DisplayName = "Administrator"
            });
        context.Wishlists.Add(new Wishlist(
                wishlistId,
                ownerId,
                "Private list",
                "PRIVATE LIST",
                WishlistOccasion.Other,
                null,
                null));
        var repository = scope.ServiceProvider.GetRequiredService<IWishlistModerationRepository>();
        repository.AddDecision(new WishlistModerationEvent(
                Guid.CreateVersion7(),
                wishlistId,
                administratorId,
                1,
                WishlistModerationAction.Suspended,
                "Private reason",
                _referenceTime.UtcDateTime));
        repository.AddDecision(new WishlistModerationEvent(
                Guid.CreateVersion7(),
                wishlistId,
                administratorId,
                2,
                WishlistModerationAction.Reactivated,
                null,
                _referenceTime.UtcDateTime));
        await context.SaveChangesAsync(cancellationToken);

        return wishlistId;
    }
}
