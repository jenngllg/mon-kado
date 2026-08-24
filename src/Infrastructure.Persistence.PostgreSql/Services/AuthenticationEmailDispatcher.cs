using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Claims and delivers pending authentication email messages.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="userRepository">The member repository.</param>
/// <param name="outboxRepository">The authentication email outbox repository.</param>
/// <param name="emailChangeRequestRepository">The member email change request repository.</param>
/// <param name="userManager">The Identity user manager.</param>
/// <param name="sender">The authentication email sender.</param>
/// <param name="timeProvider">The time provider.</param>
public class AuthenticationEmailDispatcher(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IAuthenticationEmailOutboxRepository outboxRepository,
    IMemberEmailChangeRequestRepository emailChangeRequestRepository,
    UserManager<MonKadoUser> userManager,
    IAuthenticationEmailSender sender,
    TimeProvider timeProvider) : IAuthenticationEmailDispatcher
{
    private static readonly TimeSpan _maximumRetryDelay = TimeSpan.FromHours(24);
    private static readonly TimeSpan _passwordResetLifetime = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<int> DispatchPendingAsync(
        Uri frontendOrigin,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frontendOrigin);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            leaseDuration,
            TimeSpan.Zero);

        var claimedCount = 0;
        while (claimedCount < batchSize)
        {
            var messageId = await ClaimPendingMessageAsync(
                timeProvider.GetUtcNow().UtcDateTime,
                leaseDuration,
                cancellationToken);

            if (messageId is null)
                break;

            await DeliverMessageAsync(
                messageId.Value,
                frontendOrigin,
                cancellationToken);
            claimedCount++;
        }

        return claimedCount;
    }

    private async Task<Guid?> ClaimPendingMessageAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var executionStrategy = context.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(
            token => ClaimPendingMessageOnceAsync(
                now,
                leaseDuration,
                token),
            cancellationToken);
    }

    private async Task<Guid?> ClaimPendingMessageOnceAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var message = await outboxRepository.GetNextForUpdateAsync(
            now,
            cancellationToken);

        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        message.Claim(now.Add(leaseDuration));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return message.Id;
    }

    private async Task DeliverMessageAsync(
        Guid messageId,
        Uri frontendOrigin,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var message = await outboxRepository.GetByIdForUpdateAsync(
            messageId,
            cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!CanDeliver(
            message,
            now))
            return;

        var deliverableMessage = message;
        try
        {
            var result = await SendMessageAsync(
                deliverableMessage,
                frontendOrigin,
                now,
                cancellationToken);
            deliverableMessage.MarkProcessed(
                timeProvider.GetUtcNow().UtcDateTime,
                result?.ProviderMessageId);
        }
        catch (AuthenticationEmailDeliveryException exception)
        {
            var failedAt = timeProvider.GetUtcNow().UtcDateTime;
            var retryDelay = GetRetryDelay(
                deliverableMessage.AttemptCount,
                exception.Category,
                exception.RetryAfter);
            deliverableMessage.ScheduleRetry(
                failedAt.Add(retryDelay),
                exception.Category.ToString().ToUpperInvariant());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Routes one claimed authentication message to its dedicated delivery flow.
    /// </summary>
    /// <param name="message">The claimed outbox message.</param>
    /// <param name="frontendOrigin">The trusted frontend origin.</param>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider result, or <see langword="null" /> when the message is no longer eligible.</returns>
    /// <exception cref="AuthenticationEmailDeliveryException">Thrown when the external provider rejects delivery.</exception>
    private async Task<AuthenticationEmailSendResult?> SendMessageAsync(
        AuthenticationEmailOutboxMessage message,
        Uri frontendOrigin,
        DateTime now,
        CancellationToken cancellationToken)
    {

        if (message.Kind == AuthenticationEmailKind.EmailConfirmation)
            return await SendAccountConfirmationAsync(
                message,
                frontendOrigin,
                now,
                cancellationToken);

        if (message.Kind == AuthenticationEmailKind.EmailChangeConfirmation)
            return await SendEmailChangeConfirmationAsync(
                message,
                frontendOrigin,
                now,
                cancellationToken);

        if (message.Kind == AuthenticationEmailKind.EmailChangeSecurityNotification)
            return await SendEmailChangeSecurityNotificationAsync(
                message,
                now,
                cancellationToken);

        if (message.Kind == AuthenticationEmailKind.PasswordReset)
            return await SendPasswordResetAsync(
                message,
                frontendOrigin,
                now,
                cancellationToken);

        return await SendPasswordChangedSecurityNotificationAsync(
            message,
            cancellationToken);
    }

    /// <summary>
    /// Sends a password reset link when the account still matches its request snapshot.
    /// </summary>
    /// <param name="message">The claimed outbox message.</param>
    /// <param name="frontendOrigin">The trusted frontend origin.</param>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider result, or <see langword="null" /> when the request is no longer eligible.</returns>
    /// <exception cref="AuthenticationEmailDeliveryException">Thrown when the external provider rejects delivery.</exception>
    private async Task<AuthenticationEmailSendResult?> SendPasswordResetAsync(
        AuthenticationEmailOutboxMessage message,
        Uri frontendOrigin,
        DateTime now,
        CancellationToken cancellationToken)
    {

        if (message.RecipientEmail is not { } recipientEmail ||
            message.SecurityStampSnapshot is not { } securityStampSnapshot ||
            message.CreatedAt <= now.Subtract(_passwordResetLifetime))
            return null;

        var user = await userRepository.Query()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == message.UserId,
                cancellationToken);

        if (user is null ||
            !user.EmailConfirmed ||
            !string.Equals(
                user.Email,
                recipientEmail,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                user.SecurityStamp,
                securityStampSnapshot,
                StringComparison.Ordinal))
            return null;

        var tokenCreationTime = timeProvider.GetUtcNow().UtcDateTime;

        if (message.CreatedAt <= tokenCreationTime.Subtract(_passwordResetLifetime))
            return null;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = BuildPasswordResetUrl(
            frontendOrigin,
            user.Id,
            token);

        return await sender.SendPasswordResetAsync(
            new AuthenticationPasswordResetMessage(
                message.Id,
                recipientEmail,
                resetUrl),
            cancellationToken);
    }

    private async Task<AuthenticationEmailSendResult?> SendAccountConfirmationAsync(
        AuthenticationEmailOutboxMessage message,
        Uri frontendOrigin,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.Query()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == message.UserId,
                cancellationToken);

        if (!CanReceiveAccountConfirmation(
            user,
            now))
            return null;

        var eligibleUser = user;
        ArgumentNullException.ThrowIfNull(eligibleUser.Email);
        var token = await userManager.GenerateEmailConfirmationTokenAsync(eligibleUser);
        var confirmationUrl = BuildAccountConfirmationUrl(
            frontendOrigin,
            eligibleUser.Id,
            token);

        return await sender.SendEmailConfirmationAsync(
            new AuthenticationEmailMessage(
                message.Id,
                eligibleUser.Email,
                confirmationUrl),
            cancellationToken);
    }

    private async Task<AuthenticationEmailSendResult?> SendEmailChangeConfirmationAsync(
        AuthenticationEmailOutboxMessage message,
        Uri frontendOrigin,
        DateTime now,
        CancellationToken cancellationToken)
    {

        if (message.MemberEmailChangeRequestId is not { } requestId ||
            message.RecipientEmail is not { } recipientEmail ||
            message.SecurityStampSnapshot is not { } securityStampSnapshot)
            return null;

        var request = await emailChangeRequestRepository.GetByIdAsync(
            requestId,
            cancellationToken);
        var user = await userRepository.Query()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == message.UserId,
                cancellationToken);

        if (request is null ||
            user is null ||
            !request.IsActive(now) ||
            !string.Equals(
                user.Email,
                request.CurrentEmail,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                recipientEmail,
                request.NewEmail,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                user.SecurityStamp,
                securityStampSnapshot,
                StringComparison.Ordinal))
            return null;

        var purpose = MemberEmailChangeTokenPurpose.Create(
            request.Id,
            request.NormalizedNewEmail);
        var token = await userManager.GenerateUserTokenAsync(
            user,
            EmailChangeTokenProviderOptions.ProviderName,
            purpose);
        var confirmationUrl = BuildEmailChangeConfirmationUrl(
            frontendOrigin,
            request.Id,
            token);

        return await sender.SendEmailChangeConfirmationAsync(
            new AuthenticationEmailMessage(
                message.Id,
                recipientEmail,
                confirmationUrl),
            cancellationToken);
    }

    private async Task<AuthenticationEmailSendResult?> SendEmailChangeSecurityNotificationAsync(
        AuthenticationEmailOutboxMessage message,
        DateTime now,
        CancellationToken cancellationToken)
    {

        if (message.MemberEmailChangeRequestId is not { } requestId ||
            message.RecipientEmail is not { } recipientEmail)
            return null;

        var request = await emailChangeRequestRepository.GetByIdAsync(
            requestId,
            cancellationToken);

        if (request is null ||
            request.RevokedAt is not null ||
            (request.ConfirmedAt is null && request.ExpiresAt <= now) ||
            !string.Equals(
                recipientEmail,
                request.CurrentEmail,
                StringComparison.OrdinalIgnoreCase))
            return null;

        return await sender.SendEmailChangeSecurityNotificationAsync(
            new AuthenticationEmailSecurityNotification(
                message.Id,
                recipientEmail,
                request.NewEmail),
            cancellationToken);
    }

    /// <summary>
    /// Sends a password change security notification when its recipient is available.
    /// </summary>
    /// <param name="message">The claimed outbox message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The provider result, or <see langword="null" /> when the message cannot be delivered.</returns>
    private async Task<AuthenticationEmailSendResult?> SendPasswordChangedSecurityNotificationAsync(
        AuthenticationEmailOutboxMessage message,
        CancellationToken cancellationToken)
    {

        if (message.RecipientEmail is not { } recipientEmail)
            return null;

        return await sender.SendPasswordChangedSecurityNotificationAsync(
            new AuthenticationPasswordChangedNotification(
                message.Id,
                recipientEmail,
                message.CreatedAt),
            cancellationToken);
    }

    private static bool CanReceiveAccountConfirmation(
        [NotNullWhen(true)]
        MonKadoUser? user,
        DateTime now)
    {

        return user is
        {
            EmailConfirmed: false,
            UnconfirmedAccountExpiresAt: { } expiration
        } && expiration > now;
    }

    private static bool CanDeliver(
        [NotNullWhen(true)]
        AuthenticationEmailOutboxMessage? message,
        DateTime now)
    {

        return message is
        {
            ProcessedAt: null,
            LockedUntil: { } lockedUntil
        } && lockedUntil > now;
    }

    private static Uri BuildAccountConfirmationUrl(
        Uri frontendOrigin,
        Guid userId,
        string token)
    {
        var encodedToken = AuthenticationEmailTokenEncoding.Encode(token);
        var origin = frontendOrigin.GetLeftPart(UriPartial.Authority);

        return new Uri(
            $"{origin}/confirm-email#userId={userId:D}&token={encodedToken}",
            UriKind.Absolute);
    }

    private static Uri BuildEmailChangeConfirmationUrl(
        Uri frontendOrigin,
        Guid requestId,
        string token)
    {
        var encodedToken = AuthenticationEmailTokenEncoding.Encode(token);
        var origin = frontendOrigin.GetLeftPart(UriPartial.Authority);

        return new Uri(
            $"{origin}/confirm-email-change#requestId={requestId:D}&token={encodedToken}",
            UriKind.Absolute);
    }

    /// <summary>
    /// Builds the frontend password reset URL without exposing the Identity token in the query string.
    /// </summary>
    /// <param name="frontendOrigin">The configured frontend origin.</param>
    /// <param name="userId">The member identifier.</param>
    /// <param name="token">The Identity password reset token.</param>
    /// <returns>The absolute frontend password reset URL.</returns>
    private static Uri BuildPasswordResetUrl(
        Uri frontendOrigin,
        Guid userId,
        string token)
    {
        var encodedToken = AuthenticationEmailTokenEncoding.Encode(token);
        var origin = frontendOrigin.GetLeftPart(UriPartial.Authority);

        return new Uri(
            $"{origin}/reset-password#userId={userId:D}&token={encodedToken}",
            UriKind.Absolute);
    }

    private static TimeSpan GetRetryDelay(
        int attemptCount,
        AuthenticationEmailFailureCategory category,
        TimeSpan? providerRetryAfter)
    {
        var slowRetry = category is
            AuthenticationEmailFailureCategory.Authentication or
            AuthenticationEmailFailureCategory.Permission or
            AuthenticationEmailFailureCategory.InvalidRequest or
            AuthenticationEmailFailureCategory.Unknown;
        var configuredDelay = slowRetry
            ? TimeSpan.FromHours(6)
            : attemptCount switch
            {
                <= 1 => TimeSpan.FromMinutes(1),
                2 => TimeSpan.FromMinutes(5),
                3 => TimeSpan.FromMinutes(15),
                4 => TimeSpan.FromHours(1),
                _ => TimeSpan.FromHours(6)
            };
        var requestedDelay = providerRetryAfter is { } retryAfter && retryAfter > configuredDelay
            ? retryAfter
            : configuredDelay;

        return requestedDelay > _maximumRetryDelay
            ? _maximumRetryDelay
            : requestedDelay;
    }
}
