using System.Security.Claims;
using System.Security.Cryptography;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class PostgreSqlAuthenticationTicketStore(
    IServiceScopeFactory scopeFactory,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : ITicketStore
{
    private const string ProtectionPurpose =
        "JennGllg.Fr.MonKado.Back.AuthenticationSessionTicket.v1";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken) =>
        StoreCoreAsync(ticket, cancellationToken);

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        StoreCoreAsync(ticket, cancellationToken);

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        if (!TryParseKey(key, out Guid sessionId))
        {
            return;
        }

        try
        {
            Guid userId = GetUserId(ticket);
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            byte[] protectedTicket = Protect(ticket);
            DateTimeOffset expiresAt = GetExpiration(ticket);
            DateTimeOffset now = timeProvider.GetUtcNow();

            await context.AuthenticationSessions
                .Where(session => session.Id == sessionId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(session => session.UserId, userId)
                        .SetProperty(session => session.ProtectedTicket, protectedTicket)
                        .SetProperty(session => session.RenewedAt, now)
                        .SetProperty(session => session.ExpiresAt, expiresAt),
                    cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, cancellationToken);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        if (!TryParseKey(key, out Guid sessionId))
        {
            return null;
        }

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            AuthenticationSession? session = await context.AuthenticationSessions
                .AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == sessionId, cancellationToken);

            if (session is null)
            {
                return null;
            }

            if (session.ExpiresAt <= timeProvider.GetUtcNow())
            {
                await context.AuthenticationSessions
                    .Where(value => value.Id == sessionId)
                    .ExecuteDeleteAsync(cancellationToken);
                return null;
            }

            try
            {
                return TicketSerializer.Default.Deserialize(
                    protector.Unprotect(session.ProtectedTicket));
            }
            catch (CryptographicException)
            {
                await context.AuthenticationSessions
                    .Where(value => value.Id == sessionId)
                    .ExecuteDeleteAsync(cancellationToken);
                return null;
            }
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        RetrieveAsync(key, cancellationToken);

    public Task RemoveAsync(string key) =>
        RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (!TryParseKey(key, out Guid sessionId))
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.AuthenticationSessions
                .Where(session => session.Id == sessionId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    public Task RemoveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        RemoveAsync(key, cancellationToken);

    private async Task<string> StoreCoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        Guid userId = GetUserId(ticket);
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            Guid sessionId = Guid.CreateVersion7(now);
            AuthenticationSession session = AuthenticationSession.Create(
                sessionId,
                userId,
                ticket,
                Protect(ticket),
                now);

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            context.AuthenticationSessions.Add(session);
            await context.SaveChangesAsync(cancellationToken);
            return sessionId.ToString("N");
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException("PostgreSQL", exception);
        }
    }

    private byte[] Protect(AuthenticationTicket ticket) =>
        protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static DateTimeOffset GetExpiration(AuthenticationTicket ticket) =>
        ticket.Properties.ExpiresUtc
        ?? throw new InvalidOperationException("Authentication tickets require an expiration.");

    private static Guid GetUserId(AuthenticationTicket ticket)
    {
        string? userIdValue = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out Guid userId) || userId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The authentication ticket does not contain a valid user identifier.");
        }

        return userId;
    }

    private static bool TryParseKey(string key, out Guid sessionId) =>
        Guid.TryParseExact(key, "N", out sessionId) && sessionId != Guid.Empty;
}
