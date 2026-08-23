using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class PasswordResetIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string CurrentPassword = "a long current password";
    private const string NewPassword = "a long replacement password";
    private static readonly DateTimeOffset _referenceTime = new(
        2026,
        8,
        23,
        17,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task RequestAsync_WhenAccountIsEligible_PersistsOneSecuritySnapshot()
    {
        // Arrange
        var timeProvider = new AdvancingTimeProvider(_referenceTime);
        await using var factory = await CreateFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(factory);

        // Act
        await RequestResetAsync(
            factory,
            member.Email ?? string.Empty);
        await RequestResetAsync(
            factory,
            member.Email ?? string.Empty);

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            AuthenticationEmailKind.PasswordReset,
            message.Kind);
        Assert.Equal(
            member.Id,
            message.UserId);
        Assert.Equal(
            member.Email,
            message.RecipientEmail);
        Assert.Equal(
            member.SecurityStamp,
            message.SecurityStampSnapshot);
        Assert.Null(message.MemberEmailChangeRequestId);
        Assert.Null(message.ProcessedAt);
        Assert.Equal(
            2,
            timeProvider.TimerCreationCount);
    }

    [Fact]
    public async Task RequestAsync_WhenWorkReachesMinimumDuration_DoesNotCreateDelayTimer()
    {
        // Arrange
        var timeProvider = new AdvancingTimeProvider(
            _referenceTime,
            TimeSpan.FromMilliseconds(200));
        await using var factory = await CreateFactoryAsync(timeProvider);

        // Act
        await RequestResetAsync(
            factory,
            "unknown@example.fr");

        // Assert
        Assert.Equal(
            0,
            timeProvider.TimerCreationCount);
    }

    [Fact]
    public async Task RequestAsync_WhenCancellationIsRequested_ThrowsWithoutCreatingDelayTimer()
    {
        // Arrange
        var timeProvider = new AdvancingTimeProvider(_referenceTime);
        await using var factory = await CreateFactoryAsync(timeProvider);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var action = () => service.RequestAsync(
            "member@example.fr",
            cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.Equal(
            0,
            timeProvider.TimerCreationCount);
    }

    [Fact]
    public async Task RequestAsync_WhenQuotaIsReached_EnforcesIntervalAndHourlyMaximum()
    {
        // Arrange
        var timeProvider = new AdvancingTimeProvider(_referenceTime);
        await using var factory = await CreateFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(factory);
        var email = member.Email ?? string.Empty;
        await RequestResetAsync(
            factory,
            email);
        await MarkPendingResetMessagesProcessedAsync(
            factory,
            timeProvider.GetUtcNow().UtcDateTime);

        // Act
        await RequestResetAsync(
            factory,
            email);
        var countBeforeInterval = await CountResetMessagesAsync(factory);
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        for (var requestNumber = 2; requestNumber <= 5; requestNumber++)
        {
            await RequestResetAsync(
                factory,
                email);
            await MarkPendingResetMessagesProcessedAsync(
                factory,
                timeProvider.GetUtcNow().UtcDateTime);
            timeProvider.Advance(TimeSpan.FromMinutes(1));
        }

        await RequestResetAsync(
            factory,
            email);
        var countAfterMaximum = await CountResetMessagesAsync(factory);

        // Assert
        Assert.Equal(
            1,
            countBeforeInterval);
        Assert.Equal(
            5,
            countAfterMaximum);
    }

    [Fact]
    public async Task RequestAsync_WhenPendingMessageExpires_ClosesItAndCreatesReplacement()
    {
        // Arrange
        var timeProvider = new AdvancingTimeProvider(_referenceTime);
        await using var factory = await CreateFactoryAsync(timeProvider);
        var member = await CreateMemberAsync(factory);
        var email = member.Email ?? string.Empty;
        await RequestResetAsync(
            factory,
            email);
        timeProvider.Advance(TimeSpan.FromHours(1));

        // Act
        await RequestResetAsync(
            factory,
            email);

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.CreatedAt)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            messages.Length);
        Assert.NotNull(messages[0].ProcessedAt);
        Assert.Null(messages[1].ProcessedAt);
    }

    [Fact]
    public async Task RequestAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var timeProvider = new AdvancingTimeProvider(_referenceTime);
        await using var factory = await CreateFactoryAsync(
            timeProvider,
            ConfigureUnavailableUserRepository);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();

        // Act
        var action = () => service.RequestAsync(
            "member@example.fr",
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.Equal(
            1,
            timeProvider.TimerCreationCount);
    }

    [Theory]
    [InlineData("invalid", "token")]
    [InlineData("00000000-0000-0000-0000-000000000000", "token")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "")]
    [InlineData("0198d027-51c0-7000-8000-000000000001", "!!!")]
    public async Task ResetAsync_WhenLinkInputIsMalformed_ReturnsFalse(
        string userId,
        string token)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();

        // Act
        var reset = await service.ResetAsync(
            userId,
            token,
            NewPassword,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reset);
    }

    [Fact]
    public async Task ResetAsync_WhenCancellationIsRequested_ThrowsBeforeParsingLink()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var action = () => service.ResetAsync(
            "invalid",
            "invalid",
            NewPassword,
            cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
    }

    [Fact]
    public async Task ResetAsync_WhenMemberWasDeleted_ReturnsFalse()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var token = await GenerateResetTokenAsync(
            factory,
            member.Id);
        await DeleteMemberAsync(
            factory,
            member.Id);

        // Act
        var reset = await ResetPasswordAsync(
            factory,
            member.Id,
            token,
            NewPassword);

        // Assert
        Assert.False(reset);
    }

    [Fact]
    public async Task ResetAsync_WhenMemberBecameUnconfirmed_ReturnsFalse()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var token = await GenerateResetTokenAsync(
            factory,
            member.Id);
        await SetEmailConfirmedAsync(
            factory,
            member.Id,
            emailConfirmed: false);

        // Act
        var reset = await ResetPasswordAsync(
            factory,
            member.Id,
            token,
            NewPassword);

        // Assert
        Assert.False(reset);
    }

    [Fact]
    public async Task ResetAsync_WhenTokenBelongsToAnotherMember_ReturnsFalse()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var tokenOwner = await CreateMemberAsync(factory);
        var target = await CreateSecondMemberAsync(factory);
        var token = await GenerateResetTokenAsync(
            factory,
            tokenOwner.Id);

        // Act
        var reset = await ResetPasswordAsync(
            factory,
            target.Id,
            token,
            NewPassword);

        // Assert
        Assert.False(reset);
    }

    [Fact]
    public async Task ResetAsync_WhenProviderTokenIsFresh_ReturnsTrue()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var token = await GenerateResetTokenAsync(
            factory,
            member.Id);

        // Act
        var reset = await ResetPasswordAsync(
            factory,
            member.Id,
            token,
            NewPassword);

        // Assert
        Assert.True(reset);
    }

    [Fact]
    public async Task ResetAsync_WhenProviderTokenWasCreatedAtFixedAncientDate_ReturnsFalse()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var createdAt = new DateTimeOffset(
            2000,
            1,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var token = await CreateForgedPasswordResetTokenAsync(
            factory,
            member.Id,
            createdAt);

        // Act
        var reset = await ResetPasswordAsync(
            factory,
            member.Id,
            token,
            NewPassword);

        // Assert
        Assert.False(reset);
    }

    [Fact]
    public async Task ResetAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync(
            configureServices: ConfigureUnavailableUserRepository);

        // Act
        var action = () => ResetPasswordAsync(
            factory,
            Guid.CreateVersion7(_referenceTime),
            AuthenticationEmailTokenEncoding.Encode("token"),
            NewPassword);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("unconfirmed")]
    public async Task RequestAsync_WhenAccountIsIneligible_DoesNotPersistMessage(
        string scenario)
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var email = "unknown@example.fr";

        if (scenario == "unconfirmed")
        {
            var member = await CreateMemberAsync(
                factory,
                emailConfirmed: false);
            email = member.Email ?? string.Empty;
        }

        // Act
        await RequestResetAsync(
            factory,
            email);

        // Assert
        await using var scope = factory.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task ResetAsync_WhenLinkIsValid_ChangesPasswordAndRevokesSecurityState()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var baselineSecurityStamp = member.SecurityStamp;
        var token = await GenerateResetTokenAsync(
            factory,
            member.Id);
        await CreatePendingSecurityStateAsync(
            factory,
            member);

        // Act
        var reset = await ResetPasswordAsync(
            factory,
            member.Id,
            token,
            NewPassword);
        var replayed = await ResetPasswordAsync(
            factory,
            member.Id,
            token,
            "another replacement password");

        // Assert
        Assert.True(reset);
        Assert.False(replayed);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var storedMember = await context.Users
            .AsNoTracking()
            .SingleAsync(
                user => user.Id == member.Id,
                TestContext.Current.CancellationToken);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        Assert.True(await userManager.CheckPasswordAsync(
            storedMember,
            NewPassword));
        Assert.NotEqual(
            baselineSecurityStamp,
            storedMember.SecurityStamp);
        Assert.Equal(
            0,
            storedMember.AccessFailedCount);
        Assert.Null(storedMember.LockoutEnd);
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .Where(session => session.UserId == member.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.All(
            sessions,
            session => Assert.Equal(
                _referenceTime.UtcDateTime,
                session.RevokedAt));
        var emailChangeRequest = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            emailChangeRequest.RevokedAt);
        var messages = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Where(message => message.UserId == member.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.All(
            messages.Where(message =>
                message.Kind is AuthenticationEmailKind.PasswordReset or
                    AuthenticationEmailKind.EmailChangeConfirmation or
                    AuthenticationEmailKind.EmailChangeSecurityNotification),
            message => Assert.Equal(
                _referenceTime.UtcDateTime,
                message.ProcessedAt));
        var notification = Assert.Single(
            messages,
            message => message.Kind ==
                AuthenticationEmailKind.PasswordChangedSecurityNotification);
        Assert.Equal(
            member.Email,
            notification.RecipientEmail);
        Assert.Null(notification.ProcessedAt);
    }

    [Fact]
    public async Task ResetAsync_WhenSameTokenIsUsedConcurrently_AllowsOnlyOneReset()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var token = await GenerateResetTokenAsync(
            factory,
            member.Id);
        await CreatePendingSecurityStateAsync(
            factory,
            member);

        // Act
        var results = await Task.WhenAll(
            ResetPasswordAsync(
                factory,
                member.Id,
                token,
                NewPassword),
            ResetPasswordAsync(
                factory,
                member.Id,
                token,
                NewPassword));

        // Assert
        Assert.Single(
            results,
            result => result);
        Assert.Single(
            results,
            result => !result);
        await using var scope = factory.Services.CreateAsyncScope();
        var notifications = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .Where(message => message.Kind ==
                AuthenticationEmailKind.PasswordChangedSecurityNotification)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Single(notifications);
    }

    [Fact]
    public async Task ResetAsync_WhenSeveralLinksExist_InvalidatesRemainingLinksAfterFirstReset()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var firstToken = await GenerateResetTokenAsync(
            factory,
            member.Id);
        var secondToken = await GenerateResetTokenAsync(
            factory,
            member.Id);
        var firstTokenInitiallyValid = await ValidateResetTokenAsync(
            factory,
            member.Id,
            firstToken);
        var secondTokenInitiallyValid = await ValidateResetTokenAsync(
            factory,
            member.Id,
            secondToken);

        // Act
        var firstReset = await ResetPasswordAsync(
            factory,
            member.Id,
            firstToken,
            NewPassword);
        var secondReset = await ResetPasswordAsync(
            factory,
            member.Id,
            secondToken,
            "another replacement password");

        // Assert
        Assert.NotEqual(
            firstToken,
            secondToken);
        Assert.True(firstTokenInitiallyValid);
        Assert.True(secondTokenInitiallyValid);
        Assert.True(firstReset);
        Assert.False(secondReset);
    }

    [Fact]
    public async Task ChangeAsync_WhenPasswordResetIsPending_ClosesResetMessage()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        await CreatePendingResetMessageAsync(
            factory,
            member);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemberPasswordService>();

        // Act
        var changed = await service.ChangeAsync(
            member.Id,
            CurrentPassword,
            NewPassword,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(changed);
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Kind == AuthenticationEmailKind.PasswordReset,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            message.ProcessedAt);
    }

    [Fact]
    public async Task ConfirmAsync_WhenEmailChanges_ClosesPasswordResetMessage()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        await CreatePendingSecurityStateAsync(
            factory,
            member);
        var confirmation = await GenerateEmailChangeTokenAsync(
            factory,
            member.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemberEmailChangeService>();

        // Act
        var confirmed = await service.ConfirmAsync(
            confirmation.RequestId,
            confirmation.Token,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(confirmed);
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Kind == AuthenticationEmailKind.PasswordReset,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            message.ProcessedAt);
    }

    [Fact]
    public async Task ResetAsync_WhenEmailChangesAfterTokenIsIssued_ReturnsFalse()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var baselineSecurityStamp = member.SecurityStamp;
        var token = await GenerateResetTokenAsync(
            factory,
            member.Id);
        var tokenIsInitiallyValid = await ValidateResetTokenAsync(
            factory,
            member.Id,
            token);
        await CreatePendingSecurityStateAsync(
            factory,
            member);
        var confirmation = await GenerateEmailChangeTokenAsync(
            factory,
            member.Id);
        await using var confirmationScope = factory.Services.CreateAsyncScope();
        var emailChangeService = confirmationScope.ServiceProvider
            .GetRequiredService<IMemberEmailChangeService>();
        var confirmed = await emailChangeService.ConfirmAsync(
            confirmation.RequestId,
            confirmation.Token,
            TestContext.Current.CancellationToken);

        // Act
        var reset = await ResetPasswordAsync(
            factory,
            member.Id,
            token,
            NewPassword);

        // Assert
        Assert.True(tokenIsInitiallyValid);
        Assert.True(confirmed);
        Assert.False(reset);
        await using var assertionScope = factory.Services.CreateAsyncScope();
        var storedMember = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(
            baselineSecurityStamp,
            storedMember.SecurityStamp);
    }

    [Fact]
    public async Task ResetAsync_WhenFinalPersistenceFails_RollsBackPasswordAndRevocations()
    {
        // Arrange
        await using var factory = await CreateFactoryAsync();
        var member = await CreateMemberAsync(factory);
        var baselinePasswordHash = member.PasswordHash;
        var baselineSecurityStamp = member.SecurityStamp;
        var token = await GenerateResetTokenAsync(
            factory,
            member.Id);
        await CreatePendingSecurityStateAsync(
            factory,
            member);
        await AddPasswordNotificationRejectionConstraintAsync(factory);

        try
        {
            // Act
            var action = () => ResetPasswordAsync(
                factory,
                member.Id,
                token,
                NewPassword);

            // Assert
            await Assert.ThrowsAsync<DbUpdateException>(action);
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var storedMember = await context.Users
                .AsNoTracking()
                .SingleAsync(
                    user => user.Id == member.Id,
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                baselinePasswordHash,
                storedMember.PasswordHash);
            Assert.Equal(
                baselineSecurityStamp,
                storedMember.SecurityStamp);
            Assert.Equal(
                5,
                storedMember.AccessFailedCount);
            Assert.NotNull(storedMember.LockoutEnd);
            var sessions = await context.AuthenticationSessions
                .AsNoTracking()
                .Where(session => session.UserId == member.Id)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            Assert.All(
                sessions,
                session => Assert.Null(session.RevokedAt));
            var emailChangeRequest = await context.MemberEmailChangeRequests
                .AsNoTracking()
                .SingleAsync(TestContext.Current.CancellationToken);
            Assert.Null(emailChangeRequest.RevokedAt);
            var messages = await context.AuthenticationEmailOutboxMessages
                .AsNoTracking()
                .ToArrayAsync(TestContext.Current.CancellationToken);
            Assert.All(
                messages,
                message => Assert.Null(message.ProcessedAt));
            Assert.DoesNotContain(
                messages,
                message => message.Kind ==
                    AuthenticationEmailKind.PasswordChangedSecurityNotification);
        }
        finally
        {
            await RemovePasswordNotificationRejectionConstraintAsync(factory);
        }
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider ?? new AdvancingTimeProvider(_referenceTime),
            configureServices: configureServices);
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);

        return factory;
    }

    private static void ConfigureUnavailableUserRepository(IServiceCollection services)
    {
        services.RemoveAll<IMonKadoUserRepository>();
        services.AddScoped<IMonKadoUserRepository, UnavailableMonKadoUserRepository>();
    }

    private static async Task<MonKadoUser> CreateMemberAsync(
        PostgreSqlApiFactory factory,
        bool emailConfirmed = true)
    {

        return await CreateMemberWithEmailAsync(
            factory,
            "member@example.fr",
            emailConfirmed);
    }

    private static async Task<MonKadoUser> CreateSecondMemberAsync(
        PostgreSqlApiFactory factory)
    {

        return await CreateMemberWithEmailAsync(
            factory,
            "second-member@example.fr",
            emailConfirmed: true);
    }

    private static async Task<MonKadoUser> CreateMemberWithEmailAsync(
        PostgreSqlApiFactory factory,
        string email,
        bool emailConfirmed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_referenceTime),
            Email = email,
            UserName = email,
            DisplayName = "Password reset test",
            EmailConfirmed = emailConfirmed
        };
        var result = await userManager.CreateAsync(
            member,
            CurrentPassword);
        Assert.True(
            result.Succeeded,
            string.Join(
                ", ",
                result.Errors.Select(error => error.Description)));

        return member;
    }

    private static async Task RequestResetAsync(
        PostgreSqlApiFactory factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();
        await service.RequestAsync(
            email,
            TestContext.Current.CancellationToken);
    }

    private static async Task MarkPendingResetMessagesProcessedAsync(
        PostgreSqlApiFactory factory,
        DateTime processedAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.AuthenticationEmailOutboxMessages
            .Where(message =>
                message.Kind == AuthenticationEmailKind.PasswordReset &&
                message.ProcessedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    processedAt),
                TestContext.Current.CancellationToken);
    }

    private static async Task<int> CountResetMessagesAsync(
        PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages
            .CountAsync(
                message => message.Kind == AuthenticationEmailKind.PasswordReset,
                TestContext.Current.CancellationToken);
    }

    private static async Task<string> GenerateResetTokenAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await userManager.FindByIdAsync(memberId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        var token = await userManager.GeneratePasswordResetTokenAsync(member);

        return AuthenticationEmailTokenEncoding.Encode(token);
    }

    private static async Task<string> CreateForgedPasswordResetTokenAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        DateTimeOffset createdAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await userManager.FindByIdAsync(memberId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        var userId = await userManager.GetUserIdAsync(member);
        var securityStamp = await userManager.GetSecurityStampAsync(member);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(createdAt.UtcTicks);
            writer.Write(userId);
            writer.Write(UserManager<MonKadoUser>.ResetPasswordTokenPurpose);
            writer.Write(securityStamp);
        }

        var protector = scope.ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(PasswordResetTokenProviderOptions.ProviderName);
        var identityToken = Convert.ToBase64String(protector.Protect(stream.ToArray()));

        return AuthenticationEmailTokenEncoding.Encode(identityToken);
    }

    private static async Task<bool> ValidateResetTokenAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        string encodedToken)
    {
        var decoded = AuthenticationEmailTokenEncoding.TryDecode(
            encodedToken,
            out var token);
        Assert.True(decoded);
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await userManager.FindByIdAsync(memberId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");

        return await userManager.VerifyUserTokenAsync(
            member,
            PasswordResetTokenProviderOptions.ProviderName,
            UserManager<MonKadoUser>.ResetPasswordTokenPurpose,
            token);
    }

    private static async Task DeleteMemberAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SetEmailConfirmedAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        bool emailConfirmed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(member => member.Id == memberId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.EmailConfirmed,
                    emailConfirmed),
                TestContext.Current.CancellationToken);
    }

    private static async Task<(Guid RequestId, string Token)> GenerateEmailChangeTokenAsync(
        PostgreSqlApiFactory factory,
        Guid memberId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var request = await context.MemberEmailChangeRequests
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.UserId == memberId,
                TestContext.Current.CancellationToken);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await userManager.FindByIdAsync(memberId.ToString("D"))
            ?? throw new InvalidOperationException("The member does not exist.");
        var purpose = MemberEmailChangeTokenPurpose.Create(
            request.Id,
            request.NormalizedNewEmail);
        var token = await userManager.GenerateUserTokenAsync(
            member,
            EmailChangeTokenProviderOptions.ProviderName,
            purpose);

        return (
            request.Id,
            AuthenticationEmailTokenEncoding.Encode(token));
    }

    private static async Task<bool> ResetPasswordAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        string token,
        string newPassword)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPasswordResetService>();

        return await service.ResetAsync(
            memberId.ToString("D"),
            token,
            newPassword,
            TestContext.Current.CancellationToken);
    }

    private static async Task CreatePendingSecurityStateAsync(
        PostgreSqlApiFactory factory,
        MonKadoUser member)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(user => user.Id == member.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        user => user.AccessFailedCount,
                        5)
                    .SetProperty(
                        user => user.LockoutEnd,
                        _referenceTime.AddMinutes(15)),
                TestContext.Current.CancellationToken);
        context.AuthenticationSessions.Add(AuthenticationSession.Create(
            Guid.CreateVersion7(_referenceTime.AddMilliseconds(1)),
            member.Id,
            new byte[32],
            isPersistent: false,
            _referenceTime.UtcDateTime,
            _referenceTime.UtcDateTime.AddHours(8)));
        var request = MemberEmailChangeRequest.Create(
            member.Id,
            member.Email ?? string.Empty,
            "new-member@example.fr",
            "NEW-MEMBER@EXAMPLE.FR",
            _referenceTime.UtcDateTime,
            _referenceTime.UtcDateTime.AddHours(24));
        context.MemberEmailChangeRequests.Add(request);
        context.AuthenticationEmailOutboxMessages.AddRange(
            AuthenticationEmailOutboxMessage.CreatePasswordReset(
                member.Id,
                member.Email ?? string.Empty,
                member.SecurityStamp ?? string.Empty,
                _referenceTime.UtcDateTime),
            AuthenticationEmailOutboxMessage.CreateEmailChangeConfirmation(
                request.Id,
                member.Id,
                request.NewEmail,
                _referenceTime.UtcDateTime),
            AuthenticationEmailOutboxMessage.CreateEmailChangeSecurityNotification(
                request.Id,
                member.Id,
                request.CurrentEmail,
                _referenceTime.UtcDateTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task CreatePendingResetMessageAsync(
        PostgreSqlApiFactory factory,
        MonKadoUser member)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.AuthenticationEmailOutboxMessages.Add(
            AuthenticationEmailOutboxMessage.CreatePasswordReset(
                member.Id,
                member.Email ?? string.Empty,
                member.SecurityStamp ?? string.Empty,
                _referenceTime.UtcDateTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task AddPasswordNotificationRejectionConstraintAsync(
        PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE public.authentication_email_outbox " +
            "ADD CONSTRAINT ck_test_reject_password_notification " +
            "CHECK (kind <> 'PASSWORD_CHANGED_SECURITY_NOTIFICATION');",
            TestContext.Current.CancellationToken);
    }

    private static async Task RemovePasswordNotificationRejectionConstraintAsync(
        PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE public.authentication_email_outbox " +
            "DROP CONSTRAINT IF EXISTS ck_test_reject_password_notification;",
            TestContext.Current.CancellationToken);
    }
}
