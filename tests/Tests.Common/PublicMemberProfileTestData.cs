using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Tests.Common;

public static class PublicMemberProfileTestData
{
    public static PublicMemberProfile CreateWithSharedList()
    {
        var fixture = TestFixture.Create();
        var wishlist = fixture.Build<PublicMemberWishlist>()
            .With(
                item => item.Name,
                "Discoverable list")
            .With(
                item => item.Occasion,
                WishlistOccasion.Birthday)
            .With(
                item => item.EventDate,
                new DateOnly(
                    2027,
                    12,
                    20))
            .With(
                item => item.Secret,
                new string(
                    'A',
                    43))
            .Create();

        return fixture.Build<PublicMemberProfile>()
            .With(
                profile => profile.DisplayName,
                "DistinctFixtureNameNotLogged")
            .With(
                profile => profile.Wishlists,
                [wishlist])
            .Create();
    }
}
