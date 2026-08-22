using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Updates authenticated member profiles in PostgreSQL.
/// </summary>
/// <param name="memberRepository">The member repository.</param>
/// <param name="unitOfWork">The unit of work.</param>
public class MemberProfileService(
    IMemberRepository memberRepository,
    IUnitOfWork unitOfWork) : IMemberProfileService
{
    /// <inheritdoc />
    public async Task<MemberProfile?> UpdateAsync(
        Guid memberId,
        string displayName,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var member = await memberRepository.GetForProfileUpdateAsync(
                memberId,
                cancellationToken);

            if (member is null)
                return null;

            if (member.Version != expectedVersion)
                throw new MemberProfileVersionConflictException();

            if (string.Equals(
                member.DisplayName,
                displayName,
                StringComparison.Ordinal))
            {

                return new MemberProfile(
                    member.DisplayName,
                    member.Version);
            }

            member.DisplayName = displayName;
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new MemberProfile(
                member.DisplayName,
                member.Version);
        }
        catch (DbUpdateConcurrencyException)
        {

            try
            {
                var currentMember = await memberRepository.GetForProfileUpdateAsync(
                    memberId,
                    cancellationToken);

                if (currentMember is null)
                    return null;
            }
            catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
            {

                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            throw new MemberProfileVersionConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
