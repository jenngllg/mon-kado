using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class AccountSessionService(
    MonKadoDbContext context,
    UserManager<MonKadoUser> userManager,
    SignInManager<MonKadoUser> signInManager,
    IPasswordHasher<MonKadoUser> passwordHasher,
    IAuthenticationHandlerResetter authenticationHandlerResetter,
    TimeProvider timeProvider) : IAccountSessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private static readonly TimeSpan PersistentSessionLifetime = TimeSpan.FromDays(30);

    public async Task<AccountLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            MonKadoUser? user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                PerformTimingEqualizationHash(password);
                return AccountLoginResult.InvalidCredentials;
            }

            IExecutionStrategy executionStrategy = context.Database.CreateExecutionStrategy();
            AuthenticationAttempt attempt = await executionStrategy.ExecuteAsync(() =>
                AuthenticateWithAccountLockAsync(
                    user.Id,
                    user.NormalizedEmail!,
                    password,
                    cancellationToken));
            if (attempt.Result != AccountLoginResult.Success)
            {
                return attempt.Result;
            }

            await signInManager.SignOutAsync();
            authenticationHandlerResetter.Reset(signInManager.AuthenticationScheme);
            DateTimeOffset now = timeProvider.GetUtcNow();
            await signInManager.SignInAsync(
                attempt.User!,
                new AuthenticationProperties
                {
                    AllowRefresh = !rememberMe,
                    ExpiresUtc = now.Add(rememberMe ? PersistentSessionLifetime : SessionLifetime),
                    IsPersistent = rememberMe,
                    IssuedUtc = now
                });

            return AccountLoginResult.Success;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    private async Task<AuthenticationAttempt> AuthenticateWithAccountLockAsync(
        Guid userId,
        string normalizedEmail,
        string password,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);
        FormattableString lockedUserQuery = $"""
            SELECT * FROM public.users
            WHERE id = {userId} AND normalized_email = {normalizedEmail}
            FOR UPDATE
            """;
        MonKadoUser? user = await context.Users
            .FromSqlInterpolated(lockedUserQuery)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            await transaction.CommitAsync(cancellationToken);
            PerformTimingEqualizationHash(password);
            return AuthenticationAttempt.InvalidCredentials;
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await transaction.CommitAsync(cancellationToken);
            PerformTimingEqualizationHash(password);
            return AuthenticationAttempt.InvalidCredentials;
        }

        bool passwordValid = await userManager.CheckPasswordAsync(user, password);
        if (!passwordValid)
        {
            IdentityResult failureResult = await userManager.AccessFailedAsync(user);
            EnsureIdentityUpdateSucceeded(failureResult, "record the failed login attempt");
            await transaction.CommitAsync(cancellationToken);
            return AuthenticationAttempt.InvalidCredentials;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (!user.EmailConfirmed &&
            user.UnconfirmedAccountExpiresAt is { } expiresAt &&
            expiresAt <= now)
        {
            await transaction.CommitAsync(cancellationToken);
            return AuthenticationAttempt.InvalidCredentials;
        }

        if (!user.EmailConfirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new AuthenticationAttempt(AccountLoginResult.EmailNotConfirmed, null);
        }

        if (await userManager.GetAccessFailedCountAsync(user) > 0)
        {
            IdentityResult resetResult = await userManager.ResetAccessFailedCountAsync(user);
            EnsureIdentityUpdateSucceeded(resetResult, "reset the failed login count");
        }

        await transaction.CommitAsync(cancellationToken);
        return new AuthenticationAttempt(AccountLoginResult.Success, user);
    }

    private static void EnsureIdentityUpdateSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        string errorCodes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"Unable to {operation}: {errorCodes}.");
    }

    private void PerformTimingEqualizationHash(string password)
    {
        MonKadoUser dummyUser = new()
        {
            Id = Guid.Empty,
            UserName = "timing-equalization"
        };

        _ = passwordHasher.HashPassword(dummyUser, password);
    }

    private sealed record AuthenticationAttempt(AccountLoginResult Result, MonKadoUser? User)
    {
        public static AuthenticationAttempt InvalidCredentials { get; } =
            new(AccountLoginResult.InvalidCredentials, null);
    }
}
