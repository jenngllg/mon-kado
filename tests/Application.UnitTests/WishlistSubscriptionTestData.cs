using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests;

public static class WishlistSubscriptionTestData
{
    public static WishlistSubscriptionDetails CreateDetails()
    {
        var fixture = TestFixture.Create();
        fixture.Register(() => new DateOnly(
            2026,
            12,
            24));

        return fixture.Create<WishlistSubscriptionDetails>();
    }

    public static WishlistSubscriptionPage CreatePage()
    {
        var fixture = TestFixture.Create();
        fixture.Register(CreateDetails);

        return fixture.Create<WishlistSubscriptionPage>();
    }
}
