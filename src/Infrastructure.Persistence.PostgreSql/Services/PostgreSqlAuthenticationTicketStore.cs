using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using System.Security.Claims;
using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

internal class PostgreSqlAuthenticationTicketStore(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : ITicketStore
{
    private const string ProtectionPurpose =
        "JennGllg.Fr.MonKado.Back.AuthenticationSessionTicket.v1";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
    /// <summary>
    /// Executes the store async operation.
    /// </summary>
    /// <param name="ticket">The ticket.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {

        return StoreAsync(
            ticket,
            CancellationToken.None);
    }
    /// <summary>
    /// Executes the store async operation.
    /// </summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {

        return StoreCoreAsync(
            ticket,
            cancellationToken);
    }
    /// <summary>
    /// Executes the store async operation.
    /// </summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="httpContext">The http context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {

        return StoreCoreAsync(
            ticket,
            cancellationToken);
    }
    /// <summary>
    /// Executes the renew async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="ticket">The ticket.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket)
    {

        return RenewAsync(
            key,
            ticket,
            CancellationToken.None);
    }
    /// <summary>
    /// Executes the renew async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="ticket">The ticket.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {

        if (!TryParseKey(
            key,
            out var sessionId))
            return;

        try
        {
            var userId = GetUserId(ticket);
            await using var scope = scopeFactory.CreateAsyncScope();
            var sessionRepository =
                scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRepository>();
            var protectedTicket = Protect(ticket);
            var expiresAt = GetExpiration(ticket);
            var now = timeProvider.GetUtcNow().UtcDateTime;

            await sessionRepository.UpdateAsync(
                sessionId,
                userId,
                protectedTicket,
                now,
                expiresAt,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
    /// <summary>
    /// Executes the renew async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="ticket">The ticket.</param>
    /// <param name="httpContext">The http context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {

        return RenewAsync(
            key,
            ticket,
            cancellationToken);
    }
    /// <summary>
    /// Executes the retrieve async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {

        return RetrieveAsync(
            key,
            CancellationToken.None);
    }
    /// <summary>
    /// Executes the retrieve async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken)
    {

        if (!TryParseKey(
            key,
            out var sessionId))
            return null;

        AuthenticationTicket? ticket;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sessionRepository =
                scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRepository>();
            ticket = await RetrieveSessionTicketAsync(
                sessionRepository,
                sessionId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }

        return ticket;
    }
    /// <summary>
    /// Executes the retrieve async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="httpContext">The http context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {

        return RetrieveAsync(
            key,
            cancellationToken);
    }
    /// <summary>
    /// Executes the remove async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task RemoveAsync(string key)
    {

        return RemoveAsync(
            key,
            CancellationToken.None);
    }
    /// <summary>
    /// Executes the remove async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken)
    {

        if (!TryParseKey(
            key,
            out var sessionId))
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sessionRepository =
                scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRepository>();
            await sessionRepository.DeleteAsync(
                sessionId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
    /// <summary>
    /// Executes the remove async operation.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="httpContext">The http context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public Task RemoveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {

        return RemoveAsync(
            key,
            cancellationToken);
    }

    private async Task<string> StoreCoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId(ticket);
        string key;
        try
        {
            var currentTime = timeProvider.GetUtcNow();
            var now = currentTime.UtcDateTime;
            var sessionId = Guid.CreateVersion7(currentTime);
            var session = AuthenticationSession.Create(
                sessionId,
                userId,
                ticket,
                Protect(ticket),
                now);

            await using var scope = scopeFactory.CreateAsyncScope();
            var sessionRepository =
                scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            sessionRepository.Add(session);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            key = sessionId.ToString("N");
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }

        return key;
    }

    private byte[] Protect(AuthenticationTicket ticket)
    {

        return _protector.Protect(TicketSerializer.Default.Serialize(ticket));
    }

    private async Task<AuthenticationTicket?> RetrieveSessionTicketAsync(
        IAuthenticationSessionRepository sessionRepository,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(
            sessionId,
            cancellationToken);

        if (session is null)
            return null;

        if (session.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
        {
            await sessionRepository.DeleteAsync(
                sessionId,
                cancellationToken);

            return null;
        }

        AuthenticationTicket? ticket = null;
        try
        {
            ticket = TicketSerializer.Default.Deserialize(
                _protector.Unprotect(session.ProtectedTicket));
        }
        catch (CryptographicException)
        {
            await sessionRepository.DeleteAsync(
                sessionId,
                cancellationToken);
        }

        return ticket;
    }

    private static DateTime GetExpiration(AuthenticationTicket ticket)
    {

        return ticket.Properties.ExpiresUtc?.UtcDateTime
            ?? throw new InvalidOperationException("Authentication tickets require an expiration.");
    }

    private static Guid GetUserId(AuthenticationTicket ticket)
    {
        var userIdValue = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return !Guid.TryParse(
            userIdValue,
            out var userId) || userId == Guid.Empty
            ? throw new InvalidOperationException(
                "The authentication ticket does not contain a valid user identifier.")
            : userId;
    }

    private static bool TryParseKey(
        string key,
        out Guid sessionId)
    {

        return Guid.TryParseExact(
            key,
            "N",
            out sessionId) && sessionId != Guid.Empty;
    }
}
