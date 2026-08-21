using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

internal class AccountSessionService(
    MonKadoDbContext context,
    IMonKadoUserRepository userRepository,
    UserManager<MonKadoUser> userManager,
    SignInManager<MonKadoUser> signInManager,
    IPasswordHasher<MonKadoUser> passwordHasher,
    IAuthenticationHandlerResetter authenticationHandlerResetter,
    TimeProvider timeProvider) : IAccountSessionService
{
    private static readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan _persistentSessionLifetime = TimeSpan.FromDays(30);
    /// <summary>
    /// Executes the login async operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <param name="password">The password.</param>
    /// <param name="rememberMe">The remember me.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task<AccountLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user is null)
            {
                PerformTimingEqualizationHash(password);

                return AccountLoginResult.InvalidCredentials;
            }

            var executionStrategy = context.Database.CreateExecutionStrategy();
            var attempt = await executionStrategy.ExecuteAsync(() =>
                AuthenticateWithAccountLockAsync(
                    user.Id,
                    user.NormalizedEmail!,
                    password,
                    cancellationToken));

            if (attempt.Result != AccountLoginResult.Success)
                return attempt.Result;

            await signInManager.SignOutAsync();
            authenticationHandlerResetter.Reset(signInManager.AuthenticationScheme);
            var now = timeProvider.GetUtcNow();
            await signInManager.SignInAsync(
                attempt.User!,
                new AuthenticationProperties
                {
                    AllowRefresh = !rememberMe,
                    ExpiresUtc = now.Add(rememberMe ? _persistentSessionLifetime : _sessionLifetime),
                    IsPersistent = rememberMe,
                    IssuedUtc = now
                });

            return AccountLoginResult.Success;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    internal async Task<AuthenticationAttempt> AuthenticateWithAccountLockAsync(
        Guid userId,
        string normalizedEmail,
        string password,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await userRepository.GetByIdForUpdateAsync(
            userId,
            normalizedEmail,
            cancellationToken);

        if (await CommitIfUserIsMissingAsync(
            user,
            transaction,
            cancellationToken))
        {
            PerformTimingEqualizationHash(password);

            return AuthenticationAttempt.InvalidCredentials;
        }

        var existingUser = user!;

        if (await userManager.IsLockedOutAsync(existingUser))
        {
            await transaction.CommitAsync(cancellationToken);
            PerformTimingEqualizationHash(password);

            return AuthenticationAttempt.InvalidCredentials;
        }

        var passwordValid = await userManager.CheckPasswordAsync(
            existingUser,
            password);

        if (!passwordValid)
        {
            var failureResult = await userManager.AccessFailedAsync(existingUser);
            EnsureIdentityUpdateSucceeded(
                failureResult,
                "record the failed login attempt");
            await transaction.CommitAsync(cancellationToken);

            return AuthenticationAttempt.InvalidCredentials;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (!existingUser.EmailConfirmed &&
            existingUser.UnconfirmedAccountExpiresAt is { } expiresAt &&
            expiresAt <= now)
        {
            await transaction.CommitAsync(cancellationToken);

            return AuthenticationAttempt.InvalidCredentials;
        }

        if (!existingUser.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);

            return new AuthenticationAttempt(
                AccountLoginResult.EmailNotConfirmed,
                null);
        }

        if (await userManager.GetAccessFailedCountAsync(existingUser) > 0)
        {
            var resetResult = await userManager.ResetAccessFailedCountAsync(existingUser);
            EnsureIdentityUpdateSucceeded(
                resetResult,
                "reset the failed login count");
        }

        await transaction.CommitAsync(cancellationToken);

        return new AuthenticationAttempt(
            AccountLoginResult.Success,
            existingUser);
    }

    internal static void EnsureIdentityUpdateSucceeded(
        IdentityResult result,
        string operation)
    {

        if (result.Succeeded)
            return;

        var errorCodes = string.Join(
            ", ",
            result.Errors.Select(error => error.Code));

        throw new InvalidOperationException($"Unable to {operation}: {errorCodes}.");
    }

    internal static async Task<bool> CommitIfUserIsMissingAsync(
        MonKadoUser? user,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {

        if (user is not null)
            return false;

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private void PerformTimingEqualizationHash(string password)
    {
        var dummyUser = new MonKadoUser
        {
            Id = Guid.Empty,
            UserName = "timing-equalization"
        };

        _ = passwordHasher.HashPassword(
            dummyUser,
            password);
    }

}
