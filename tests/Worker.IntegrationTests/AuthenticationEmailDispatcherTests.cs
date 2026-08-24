using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using System.Collections.Concurrent;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

[Collection(PostgreSqlWorkerTestSuite.Name)]
public class AuthenticationEmailDispatcherTests(PostgreSqlWorkerFixture fixture) : IDisposable
{
    private readonly string _keysPath = Path.Combine(
        Path.GetTempPath(),
        "mon-kado-worker-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_WhenAcceptedMessage_IsProcessedAndProviderIdentifierIsStored()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        var messageId = await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        // Act
        var message = await context.AuthenticationEmailOutboxMessages
            .SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            messageId,
            sender.Messages.Single().OutboxMessageId);
        Assert.Equal(
            "fake-provider-id",
            message.ProviderMessageId);
        Assert.Equal(
            now.UtcDateTime,
            message.ProcessedAt);
        Assert.Null(message.LockedUntil);
        Assert.Null(message.LastError);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransientFailureReleasesLeaseAndSchedulesFirstRetry_Completes()
    {
        // Arrange
        var sender = new FakeEmailSender(fail: true);
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        // Act
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            1,
            message.AttemptCount);
        Assert.Equal(
            now.UtcDateTime.AddMinutes(1),
            message.AvailableAt);
        Assert.Equal(
            "TRANSIENT",
            message.LastError);
        Assert.Null(message.LockedUntil);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProviderRetryAfterOverridesShorterConfiguredDelay_Completes()
    {
        // Arrange
        var sender = new FakeEmailSender(
            fail: true,
            retryAfter: TimeSpan.FromHours(2));
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        // Act
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            now.AddHours(2),
            message.AvailableAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAuthenticationFailure_UsesSlowRetryImmediately()
    {
        // Arrange
        var sender = new FakeEmailSender(
            fail: true,
            failureCategory: AuthenticationEmailFailureCategory.Authentication);
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        // Act
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            now.AddHours(6),
            message.AvailableAt);
        Assert.Equal(
            "AUTHENTICATION",
            message.LastError);
    }

    [Theory]
    [InlineData(2, AuthenticationEmailFailureCategory.Transient, null, 5)]
    [InlineData(3, AuthenticationEmailFailureCategory.Transient, 1, 15)]
    [InlineData(4, AuthenticationEmailFailureCategory.Transient, null, 60)]
    [InlineData(5, AuthenticationEmailFailureCategory.Transient, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.RateLimited, null, 1)]
    [InlineData(1, AuthenticationEmailFailureCategory.Permission, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.InvalidRequest, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.Unknown, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.Transient, 1_500, 1_440)]
    public async Task ExecuteAsync_WhenDeliveryFails_UsesExpectedRetryDelay(
        int attemptCount,
        AuthenticationEmailFailureCategory failureCategory,
        int? providerRetryMinutes,
        int expectedDelayMinutes)
    {
        // Arrange
        var sender = new FakeEmailSender(
            fail: true,
            retryAfter: providerRetryMinutes is null
                ? null
                : TimeSpan.FromMinutes(providerRetryMinutes.Value),
            failureCategory: failureCategory);
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.AttemptCount,
                    attemptCount - 1),
                TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        await using var scope = provider.CreateAsyncScope();
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            attemptCount,
            message.AttemptCount);
        Assert.Equal(
            now.AddMinutes(expectedDelayMinutes),
            message.AvailableAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExpiredLease_CanBeClaimedAgain()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext =
                setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        message => message.LockedUntil,
                        now.UtcDateTime.AddMinutes(-1))
                    .SetProperty(
                        message => message.AttemptCount,
                        1),
                TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Single(sender.Messages);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            message.AttemptCount);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentDispatchersClaimMessageOnlyOnce_Completes()
    {
        // Arrange
        var sender = new FakeEmailSender(delay: TimeSpan.FromMilliseconds(100));
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        // Act
        await Task.WhenAll(
            DispatchAsync(provider),
            DispatchAsync(provider));

        // Assert
        Assert.Single(sender.Messages);
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("expired")]
    [InlineData("missing-account")]
    public async Task ExecuteAsync_WhenAccountCannotReceiveConfirmation_IsNotContactedAndMessageIsClosed(
        string scenario)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext =
                setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            switch (scenario)
            {
                case "confirmed":
                    await setupContext.Users.ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            user => user.EmailConfirmed,
                            true),
                        TestContext.Current.CancellationToken);
                    break;
                case "expired":
                    await setupContext.Users.ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            user => user.UnconfirmedAccountExpiresAt,
                            now.UtcDateTime),
                        TestContext.Current.CancellationToken);
                    break;
                case "missing-account":
                    await setupContext.Database.OpenConnectionAsync(
                        TestContext.Current.CancellationToken);

                    try
                    {
                        await setupContext.Database.ExecuteSqlRawAsync(
                            "SET session_replication_role = replica;",
                            TestContext.Current.CancellationToken);
                        await setupContext.Users.ExecuteDeleteAsync(
                            TestContext.Current.CancellationToken);
                    }
                    finally
                    {
                        await setupContext.Database.ExecuteSqlRawAsync(
                            "SET session_replication_role = origin;",
                            TestContext.Current.CancellationToken);
                        await setupContext.Database.CloseConnectionAsync();
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown test scenario '{scenario}'.");
            }
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.Messages);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            now.UtcDateTime,
            message.ProcessedAt);
    }

    [Theory]
    [InlineData("missing-message")]
    [InlineData("processed")]
    [InlineData("missing-lease")]
    [InlineData("expired-lease")]
    public async Task ExecuteAsync_WhenClaimedMessageBecomesUndeliverable_DoesNotContactProvider(
        string scenario)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await CreateClaimMutationTriggerAsync(
            provider,
            scenario);

        // Act
        var claimedCount = await DispatchOneAsync(provider);

        // Assert
        Assert.Equal(
            1,
            claimedCount);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmailChangeMessagesArePending_SendsBothMessagesToTheirSnapshotRecipients()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        var requestId = await CreateMemberEmailChangeRequestAsync(
            provider,
            now);

        // Act
        await DispatchAsync(provider);

        // Assert
        var confirmation = Assert.Single(sender.EmailChangeConfirmations);
        Assert.Equal(
            "new-member@example.fr",
            confirmation.RecipientAddress);
        Assert.Equal(
            "/confirm-email-change",
            confirmation.ConfirmationUrl.AbsolutePath);
        Assert.Contains(
            $"requestId={requestId:D}",
            confirmation.ConfirmationUrl.Fragment,
            StringComparison.Ordinal);
        var notification = Assert.Single(sender.EmailChangeNotifications);
        Assert.Equal(
            "member@example.fr",
            notification.RecipientAddress);
        Assert.Equal(
            "new-member@example.fr",
            notification.RequestedAddress);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPasswordChangedNotificationIsPending_SendsSnapshotNotification()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            23,
            10,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        var messageId = await CreatePasswordChangedNotificationAsync(
            provider,
            now);

        // Act
        await DispatchAsync(provider);

        // Assert
        var notification = Assert.Single(sender.PasswordChangedNotifications);
        Assert.Equal(
            messageId,
            notification.OutboxMessageId);
        Assert.Equal(
            "member@example.fr",
            notification.RecipientAddress);
        Assert.Equal(
            now.UtcDateTime,
            notification.ChangedAt);
        Assert.Empty(sender.Messages);
        Assert.Empty(sender.EmailChangeConfirmations);
        Assert.Empty(sender.EmailChangeNotifications);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPasswordResetIsEligible_SendsValidSnapshotLink()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            23,
            10,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        var messageId = await CreatePasswordResetAsync(
            provider,
            now);

        // Act
        await DispatchAsync(provider);

        // Assert
        var delivery = Assert.Single(sender.PasswordResetMessages);
        Assert.Equal(
            messageId,
            delivery.OutboxMessageId);
        Assert.Equal(
            "member@example.fr",
            delivery.RecipientAddress);
        Assert.Equal(
            "/reset-password",
            delivery.ResetUrl.AbsolutePath);
        var fragment = GetFragmentValues(delivery.ResetUrl);
        var userId = Guid.Parse(fragment["userId"]);
        var token = DecodeBase64Url(fragment["token"]);
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        Assert.True(await userManager.VerifyUserTokenAsync(
            user,
            PasswordResetTokenProviderOptions.ProviderName,
            UserManager<MonKadoUser>.ResetPasswordTokenPurpose,
            token));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("unconfirmed")]
    [InlineData("changed-email")]
    [InlineData("changed-security-stamp")]
    [InlineData("missing-member")]
    public async Task ExecuteAsync_WhenPasswordResetSnapshotIsStale_ClosesWithoutSending(
        string scenario)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            23,
            10,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreatePasswordResetAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            if (scenario == "expired")
                await context.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            message => message.CreatedAt,
                            now.UtcDateTime.AddHours(-1))
                        .SetProperty(
                            message => message.AvailableAt,
                            now.UtcDateTime.AddHours(-1)),
                    TestContext.Current.CancellationToken);

            if (scenario == "unconfirmed")
                await context.Users.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.EmailConfirmed,
                        false),
                    TestContext.Current.CancellationToken);

            if (scenario == "changed-email")
                await context.Users.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.Email,
                        "changed@example.fr"),
                    TestContext.Current.CancellationToken);

            if (scenario == "changed-security-stamp")
                await context.Users.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.SecurityStamp,
                        "changed-security-stamp"),
                    TestContext.Current.CancellationToken);

            if (scenario == "missing-member")
            {
                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE public.authentication_email_outbox " +
                    "DROP CONSTRAINT fk_authentication_email_outbox_users_user_id;",
                    TestContext.Current.CancellationToken);
                await context.Users.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            }
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.PasswordResetMessages);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            now.UtcDateTime,
            message.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPasswordResetExpiresDuringPreparation_ClosesWithoutTokenGeneration()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            23,
            10,
            0,
            0,
            TimeSpan.Zero);
        var timeProvider = new StepTimeProvider(now);
        await using var provider = await CreateProviderAsync(
            sender,
            now,
            timeProvider);
        await CreatePasswordResetAsync(
            provider,
            now);
        // Claiming the auditable outbox message consumes two clock reads before delivery begins.
        timeProvider.AdvanceOnRead(
            4,
            TimeSpan.FromHours(1));

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.PasswordResetMessages);
        await using var scope = provider.CreateAsyncScope();
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(message.ProcessedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WhenPasswordResetSnapshotIsIncomplete_ClosesWithoutSending(
        bool removesRecipient)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            23,
            10,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreatePasswordResetAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE public.authentication_email_outbox " +
                "DROP CONSTRAINT ck_authentication_email_outbox_email_change_fields_consistent;",
                TestContext.Current.CancellationToken);

            if (removesRecipient)
                await context.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        message => message.RecipientEmail,
                        (string?)null),
                    TestContext.Current.CancellationToken);

            if (!removesRecipient)
                await context.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        message => message.SecurityStampSnapshot,
                        (string?)null),
                    TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.PasswordResetMessages);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPasswordChangedNotificationHasNoRecipient_ClosesWithoutSending()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            23,
            10,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreatePasswordChangedNotificationAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE public.authentication_email_outbox " +
                "DROP CONSTRAINT ck_authentication_email_outbox_email_change_fields_consistent;",
                TestContext.Current.CancellationToken);
            await context.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.RecipientEmail,
                    (string?)null),
                TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.PasswordChangedNotifications);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(message.ProcessedAt);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("changed-current-email")]
    public async Task ExecuteAsync_WhenEmailChangeRequestCannotBeConfirmed_ClosesConfirmationWithoutSending(
        string scenario)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateMemberEmailChangeRequestAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            if (scenario == "revoked")
                await setupContext.MemberEmailChangeRequests.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        request => request.RevokedAt,
                        now.UtcDateTime),
                    TestContext.Current.CancellationToken);

            if (scenario == "expired")
                await setupContext.MemberEmailChangeRequests.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            request => request.CreatedAt,
                            now.UtcDateTime.AddHours(-2))
                        .SetProperty(
                            request => request.ExpiresAt,
                            now.UtcDateTime.AddHours(-1)),
                    TestContext.Current.CancellationToken);

            if (scenario == "changed-current-email")
                await setupContext.Users.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        user => user.Email,
                        "changed@example.fr"),
                    TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.EmailChangeConfirmations);
    }

    [Theory]
    [InlineData(AuthenticationEmailKind.EmailChangeConfirmation, true)]
    [InlineData(AuthenticationEmailKind.EmailChangeConfirmation, false)]
    [InlineData(AuthenticationEmailKind.EmailChangeSecurityNotification, true)]
    [InlineData(AuthenticationEmailKind.EmailChangeSecurityNotification, false)]
    public async Task ExecuteAsync_WhenEmailChangeMessageIsIncomplete_ClosesMessageWithoutSending(
        AuthenticationEmailKind kind,
        bool removesRequestId)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateMemberEmailChangeRequestAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var context = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.AuthenticationEmailOutboxMessages
                .Where(message => message.Kind != kind)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE public.authentication_email_outbox " +
                "DROP CONSTRAINT ck_authentication_email_outbox_email_change_fields_consistent;",
                TestContext.Current.CancellationToken);

            if (removesRequestId)
                await context.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        message => message.MemberEmailChangeRequestId,
                        (Guid?)null),
                    TestContext.Current.CancellationToken);

            if (!removesRequestId)
                await context.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        message => message.RecipientEmail,
                        (string?)null),
                    TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.EmailChangeConfirmations);
        Assert.Empty(sender.EmailChangeNotifications);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSeparateInstanceSharingKeys_CanValidateGeneratedAccountConfirmationToken()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var sendingProvider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            sendingProvider,
            now);
        await DispatchAsync(sendingProvider);

        await using var validatingProvider = BuildProvider(
            new FakeEmailSender(),
            now);
        var delivery = sender.Messages.Single();
        var fragment = delivery.ConfirmationUrl.Fragment
            .TrimStart('#')
            .Split(
                '&',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(
                '=',
                2))
            .ToDictionary(
                parts => parts[0],
                parts => parts[1],
                StringComparer.Ordinal);
        var userId = Guid.Parse(fragment["userId"]);
        var token = DecodeBase64Url(fragment["token"]);

        // Act
        await using var scope = validatingProvider.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");

        // Assert
        Assert.True(await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<MonKadoUser>.ConfirmEmailTokenPurpose,
            token));
    }

    [Fact]
    public async Task ExecuteAsync_WhenSeparateInstanceSharingKeys_CanValidateGeneratedEmailChangeToken()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var sendingProvider = await CreateProviderAsync(
            sender,
            now);
        var requestId = await CreateMemberEmailChangeRequestAsync(
            sendingProvider,
            now);
        await DispatchAsync(sendingProvider);
        await using var validatingProvider = BuildProvider(
            new FakeEmailSender(),
            now);
        var delivery = Assert.Single(sender.EmailChangeConfirmations);
        var fragment = delivery.ConfirmationUrl.Fragment
            .TrimStart('#')
            .Split(
                '&',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(
                '=',
                2))
            .ToDictionary(
                parts => parts[0],
                parts => parts[1],
                StringComparer.Ordinal);
        var deliveredRequestId = Guid.Parse(fragment["requestId"]);
        var token = DecodeBase64Url(fragment["token"]);
        await using var scope = validatingProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == requestId,
                TestContext.Current.CancellationToken);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = await userManager.FindByIdAsync(request.UserId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        var purpose = MemberEmailChangeTokenPurpose.Create(
            request.Id,
            request.NormalizedNewEmail);

        // Act
        var isValid = await userManager.VerifyUserTokenAsync(
            user,
            EmailChangeTokenProviderOptions.ProviderName,
            purpose,
            token);

        // Assert
        Assert.Equal(
            requestId,
            deliveredRequestId);
        Assert.True(isValid);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSeparateInstanceSharingKeys_CanResetPasswordFromGeneratedLink()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            23,
            10,
            0,
            0,
            TimeSpan.Zero);
        await using var sendingProvider = await CreateProviderAsync(
            sender,
            now);
        await CreatePasswordResetAsync(
            sendingProvider,
            now);
        await DispatchAsync(sendingProvider);
        var delivery = Assert.Single(sender.PasswordResetMessages);
        var fragment = GetFragmentValues(delivery.ResetUrl);
        var userId = Guid.Parse(fragment["userId"]);
        var token = fragment["token"];
        await using var validatingProvider = BuildProvider(
            new FakeEmailSender(),
            now);
        await using var scope = validatingProvider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();

        // Act
        var reset = await service.ResetAsync(
            userId.ToString("D"),
            token,
            "a long replacement password",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reset);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        Assert.True(await userManager.CheckPasswordAsync(
            user,
            "a long replacement password"));
    }

    public void Dispose()
    {

        if (Directory.Exists(_keysPath))
            Directory.Delete(
                _keysPath,
                recursive: true);

        GC.SuppressFinalize(this);
    }

    private async Task<ServiceProvider> CreateProviderAsync(
        FakeEmailSender sender,
        DateTimeOffset now,
        TimeProvider? timeProvider = null)
    {
        var provider = BuildProvider(
            sender,
            now,
            timeProvider);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return provider;
    }

    private ServiceProvider BuildProvider(
        FakeEmailSender sender,
        DateTimeOffset now,
        TimeProvider? timeProvider = null)
    {
        Directory.CreateDirectory(_keysPath);
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();
        configuration["DataProtection:KeysPath"] = _keysPath;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(
            timeProvider ?? new FixedTimeProvider(now));
        services.AddSingleton<IAuthenticationEmailSender>(sender);
        services.ConfigureDataProtection(
            configuration,
            new TestHostEnvironment());
        services.ConfigureInfrastructureInjection(configuration);
        services.ConfigureAuthenticationEmailDelivery();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string DecodeBase64Url(string token)
    {
        var base64 = token.Replace(
            '-',
            '+').Replace(
                '_',
                '/');
        base64 = base64.PadRight(
            base64.Length + ((4 - (base64.Length % 4)) % 4),
            '=');

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static Dictionary<string, string> GetFragmentValues(Uri uri)
    {

        return uri.Fragment
            .TrimStart('#')
            .Split(
                '&',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(
                '=',
                2))
            .ToDictionary(
                parts => parts[0],
                parts => parts[1],
                StringComparer.Ordinal);
    }

    private static async Task<Guid> CreateUnconfirmedAccountAsync(
        ServiceProvider provider,
        DateTimeOffset now)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser()
        {
            Id = Guid.CreateVersion7(now),
            Email = "member@example.fr",
            UserName = "member@example.fr",
            DisplayName = "Member",
            UnconfirmedAccountExpiresAt = now.UtcDateTime.AddDays(30)
        };
        var result = await userManager.CreateAsync(user);
        Assert.True(result.Succeeded);

        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var message =
            AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
                user.Id,
                now.UtcDateTime);
        context.AuthenticationEmailOutboxMessages.Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return message.Id;
    }

    private static async Task<Guid> CreateMemberEmailChangeRequestAsync(
        ServiceProvider provider,
        DateTimeOffset now)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser()
        {
            Id = Guid.CreateVersion7(now),
            Email = "member@example.fr",
            UserName = "member@example.fr",
            EmailConfirmed = true,
            DisplayName = "Member"
        };
        var result = await userManager.CreateAsync(user);
        Assert.True(result.Succeeded);
        var securityStamp = Assert.IsType<string>(user.SecurityStamp);
        var request = MemberEmailChangeRequest.Create(
            user.Id,
            "member@example.fr",
            "new-member@example.fr",
            "NEW-MEMBER@EXAMPLE.FR",
            now.UtcDateTime,
            now.UtcDateTime.AddHours(24));
        var confirmation = AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
            request.Id,
            user.Id,
            request.NewEmail,
            securityStamp,
            now.UtcDateTime);
        var notification = AuthenticationEmailOutboxMessage.CreateEmailChangeSecurityNotification(
            request.Id,
            user.Id,
            request.CurrentEmail,
            now.UtcDateTime);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.MemberEmailChangeRequests.Add(request);
        context.AuthenticationEmailOutboxMessages.AddRange(
            confirmation,
            notification);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return request.Id;
    }

    private static async Task<Guid> CreatePasswordChangedNotificationAsync(
        ServiceProvider provider,
        DateTimeOffset now)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(now),
            Email = "member@example.fr",
            UserName = "member@example.fr",
            EmailConfirmed = true,
            DisplayName = "Member"
        };
        var result = await userManager.CreateAsync(user);
        Assert.True(result.Succeeded);
        var message = AuthenticationEmailOutboxMessage
            .CreatePasswordChangedSecurityNotification(
                user.Id,
                "member@example.fr",
                now.UtcDateTime);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.AuthenticationEmailOutboxMessages.Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return message.Id;
    }

    private static async Task<Guid> CreatePasswordResetAsync(
        ServiceProvider provider,
        DateTimeOffset now)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(now),
            Email = "member@example.fr",
            UserName = "member@example.fr",
            EmailConfirmed = true,
            DisplayName = "Member"
        };
        var result = await userManager.CreateAsync(
            user,
            "a valid member password");
        Assert.True(result.Succeeded);
        var securityStamp = user.SecurityStamp
            ?? throw new InvalidOperationException("The member security stamp is missing.");
        var message = AuthenticationEmailOutboxMessage.CreatePasswordReset(
            user.Id,
            "member@example.fr",
            securityStamp,
            now.UtcDateTime);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.AuthenticationEmailOutboxMessages.Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return message.Id;
    }

    private static async Task DispatchAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var dispatcher =
            scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();
        await dispatcher.DispatchPendingAsync(
            new Uri("https://mon-kado.fr"),
            20,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);
    }

    private static async Task<int> DispatchOneAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var dispatcher =
            scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();

        return await dispatcher.DispatchPendingAsync(
            new Uri("https://mon-kado.fr"),
            1,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);
    }

    private static async Task CreateClaimMutationTriggerAsync(
        ServiceProvider provider,
        string scenario)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var command = scenario switch
        {
            "missing-message" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    DELETE FROM public.authentication_email_outbox WHERE id = NEW.id;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                AFTER UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            "processed" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    NEW.processed_at = NEW.locked_until;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                BEFORE UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            "missing-lease" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    NEW.locked_until = NULL;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                BEFORE UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            "expired-lease" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    NEW.locked_until = NEW.available_at;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                BEFORE UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            _ => throw new InvalidOperationException($"Unknown test scenario '{scenario}'.")
        };
        await context.Database.ExecuteSqlRawAsync(
            command,
            TestContext.Current.CancellationToken);
    }

}
