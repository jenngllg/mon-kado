using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Tests.Common;

public static class WishlistModerationTestData
{
    public static Wishlist CreateActiveWishlist()
    {
        var fixture = TestFixture.Create();
        fixture.Register<DateOnly?>(() => null);

        return fixture.Create<Wishlist>();
    }

    public static WishlistModerationEmailMessage CreateSuspensionEmail()
    {

        return CreateEmail(
            WishlistModerationAction.Suspended,
            "Private reason <script>not markup</script>");
    }

    public static WishlistModerationEmailMessage CreateReasonAmendmentEmail()
    {

        return CreateEmail(
            WishlistModerationAction.ReasonUpdated,
            "Amended private reason");
    }

    public static WishlistModerationEmailMessage CreateReactivationEmail()
    {

        return CreateEmail(
            WishlistModerationAction.Reactivated,
            null);
    }

    public static WishlistModerationEmailMessage CreateUnknownDecisionEmail()
    {

        return CreateEmail(
            (WishlistModerationAction)int.MaxValue,
            null);
    }

    private static WishlistModerationEmailMessage CreateEmail(
        WishlistModerationAction action,
        string? reason)
    {
        var fixture = TestFixture.Create();

        return fixture
            .Build<WishlistModerationEmailMessage>()
            .With(
            message => message.Action,
            action)
            .With(
            message => message.Reason,
            reason)
            .With(
            message => message.WishlistName,
            "Private wishlist <strong>not markup</strong>")
            .With(
            message => message.RecipientAddress,
            "owner@example.test")
            .With(
            message => message.OccurredAt,
            DateTime.UnixEpoch)
            .Create();
    }
}
