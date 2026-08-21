using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

internal class AccountRegistrationService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IAuthenticationEmailOutboxRepository outboxRepository,
    UserManager<MonKadoUser> userManager,
    IPasswordHasher<MonKadoUser> passwordHasher,
    TimeProvider timeProvider) : IAccountRegistrationService
{
    private const string DuplicateEmailErrorCode = "DuplicateEmail";
    private const string DuplicateUserNameErrorCode = "DuplicateUserName";
    private const string NormalizedEmailIndexName = "ux_users_normalized_email";
    private const string NormalizedUserNameIndexName = "ux_users_normalized_user_name";
    private static readonly TimeSpan _unconfirmedAccountLifetime = TimeSpan.FromDays(30);
    /// <summary>
    /// Executes the register async operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <param name="password">The password.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        try
        {
            await IgnoreDuplicateAccountAsync(async () =>
            {
                var executionStrategy = context.Database.CreateExecutionStrategy();
                await executionStrategy.ExecuteAsync(async () =>
                {
                    // A retry must not reuse tracked state left by an interrupted transaction.
                    context.ChangeTracker.Clear();
                    var existingUser = await userManager.FindByEmailAsync(email);

                    if (existingUser is not null)
                    {
                        PerformTimingEqualizationHash(password);

                        return;
                    }

                    await using var transaction =
                        await context.Database.BeginTransactionAsync(cancellationToken);

                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    var user = new MonKadoUser()
                    {
                        Id = Guid.CreateVersion7(now),
                        Email = email,
                        UserName = email,
                        DisplayName = displayName,
                        EmailConfirmed = false,
                        UnconfirmedAccountExpiresAt = now.Add(_unconfirmedAccountLifetime),
                        Version = 1
                    };

                    var result = await userManager.CreateAsync(
                        user,
                        password);

                    if (!await CanContinueAfterCreationAsync(
                        result,
                        transaction,
                        cancellationToken))
                        return;

                    outboxRepository.Add(
                        AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
                            user.Id,
                            now));
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                });
            });
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    private void PerformTimingEqualizationHash(string password)
    {
        var dummyUser = new MonKadoUser()
        {
            Id = Guid.Empty,
            UserName = "timing-equalization"
        };

        _ = passwordHasher.HashPassword(
            dummyUser,
            password);
    }

    internal static bool IsDuplicateAccount(IdentityResult result)
    {

        return result.Errors.Any(error =>
            error.Code is DuplicateEmailErrorCode or DuplicateUserNameErrorCode);
    }

    internal static async Task IgnoreDuplicateAccountAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (DbUpdateException exception) when (IsDuplicateAccount(exception))
        {
            // A concurrent request committed the same normalized email first.
        }
    }

    internal static async Task<bool> CanContinueAfterCreationAsync(
        IdentityResult result,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {

        if (result.Succeeded)
            return true;

        if (IsDuplicateAccount(result))
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        var errorCodes = string.Join(
            ',',
            result.Errors.Select(error => error.Code));

        throw new InvalidOperationException($"ASP.NET Core Identity rejected account creation: {errorCodes}.");
    }

    internal static bool IsDuplicateAccount(DbUpdateException exception)
    {

        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NormalizedEmailIndexName or NormalizedUserNameIndexName
        };
    }
}
