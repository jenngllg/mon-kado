using AutoFixture;

using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Tests.Common;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Creates isolated subscription identities.</summary>
public static class WishlistSubscriptionTestData
{
    /// <summary>Creates a confirmed member without unrelated identity navigation data.</summary>
    /// <returns>The isolated member.</returns>
    public static MonKadoUser CreateMember()
    {
        var fixture = TestFixture.Create();
        var id = fixture.Create<Guid>();
        var email = $"subscriber-{id:N}@example.test";

        return fixture.Build<MonKadoUser>()
            .OmitAutoProperties()
            .With(
                member => member.Id,
                id)
            .With(
                member => member.DisplayName,
                "Member")
            .With(
                member => member.UserName,
                email)
            .With(
                member => member.NormalizedUserName,
                email.ToUpperInvariant())
            .With(
                member => member.Email,
                email)
            .With(
                member => member.NormalizedEmail,
                email.ToUpperInvariant())
            .With(
                member => member.EmailConfirmed,
                true)
            .With(
                member => member.SecurityStamp,
                fixture.Create<Guid>().ToString())
            .Create();
    }
}
