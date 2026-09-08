using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>Shares the active-request slot and rolling member quota across both export entry points.</summary>
/// <param name="context">The caller's transactional database context.</param>
/// <param name="options">The stable generation limits.</param>
public class PersonalDataExportRequestRepository(
    MonKadoDbContext context,
    IOptions<PersonalDataExportOptions> options) : IPersonalDataExportRequestRepository
{
    /// <inheritdoc/>
    public async Task LockAccountsAsync(
        Guid administratorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var identifiers = new[]
        {
            administratorId,
            memberId
        };
        await context.Users
            .FromSqlInterpolated($"SELECT *, xmin FROM public.users WHERE id = ANY({identifiers}) ORDER BY id FOR UPDATE")
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MemberDataExport> GetOrCreateAsync(
        Guid memberId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var active = await context.MemberDataExports.SingleOrDefaultAsync(
            export => export.MemberId == memberId && (export.Status == PersonalDataExportStatus.Queued || export.Status == PersonalDataExportStatus.Processing || export.Status == PersonalDataExportStatus.Ready),
            cancellationToken);
        active?.Expire(now);

        if (active is not null && active.Status is not PersonalDataExportStatus.Expired)
            return active;
        var cutoff = now - options.Value.RequestWindow;
        var count = await context.MemberDataExports.CountAsync(
            export => export.MemberId == memberId && export.CreatedAt > cutoff,
            cancellationToken);

        if (count >= options.Value.MaximumRequests)
            throw new PersonalDataExportRateLimitException();

        // Release the expired unique slot before inserting its successor in the same transaction.
        if (active is not null)
        {
            await context.MemberDataExports
                .Where(export => export.Id == active.Id)
                .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    export => export.Status,
                    PersonalDataExportStatus.Expired),
                cancellationToken);
            context
                .Entry(active)
                .State = EntityState.Unchanged;
        }

        var request = new MemberDataExport(
            memberId,
            now);
        context.MemberDataExports.Add(request);

        return request;
    }
}
