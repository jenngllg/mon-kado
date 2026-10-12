//go:build integration

package httpapi_test

import (
	"context"
	"errors"
	"net/url"
	"testing"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestOperations_WhenCallerCancels_PropagatesCancellationWithoutBusinessEffects(t *testing.T) {
	// Arrange: use the public service contracts with a real native PostgreSQL pool.
	f := newFixture(t)
	member := f.member(t, "cancelled-operations@example.test")
	s := f.service
	id, flow := security.NewID(), security.RandomToken()
	principal, err := s.Authenticate(t.Context(), f.access)
	if err != nil {
		t.Fatal(err)
	}
	server, err := url.Parse(f.server.URL)
	if err != nil {
		t.Fatal(err)
	}
	refresh := ""
	for _, cookie := range f.client.Jar.Cookies(server) {
		if cookie.Name == "MonKado.Refresh" {
			refresh = cookie.Value
		}
	}
	if refresh == "" {
		t.Fatal("authenticated fixture has no refresh proof")
	}
	name, email, password, occasion, quantity := "Cancelled list", "cancelled-new@example.test", "A long test password 🎁", "other", 1
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	var before int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM authentication_email_outbox`).Scan(&before); err != nil {
		t.Fatal(err)
	}
	for _, scenario := range []struct {
		name string
		call func(context.Context) error
	}{
		{"register", func(ctx context.Context) error {
			return s.Register(ctx, domain.RegisterAccountRequest{DisplayName: &name, Email: &email, Password: &password})
		}},
		{"login", func(ctx context.Context) error {
			_, err := s.Login(ctx, domain.LoginRequest{Email: &email, Password: &password}, "")
			return err
		}},
		{"refresh", func(ctx context.Context) error { _, err := s.Refresh(ctx, refresh); return err }},
		{"logout", func(ctx context.Context) error { return s.Logout(ctx, refresh) }},
		{"currentSession", func(ctx context.Context) error { _, _, err := s.CurrentSession(ctx, principal); return err }},
		{"authenticate", func(ctx context.Context) error { _, err := s.Authenticate(ctx, f.access); return err }},
		{"memberSearch", func(ctx context.Context) error { _, err := s.SearchMembers(ctx, "Test", 1, 20); return err }},
		{"publicProfile", func(ctx context.Context) error { _, err := s.PublicMemberProfile(ctx, member); return err }},
		{"updateProfile", func(ctx context.Context) error {
			_, _, err := s.UpdateProfile(ctx, member, domain.UpdateMemberProfileRequest{DisplayName: &name}, 1)
			return err
		}},
		{"updatePassword", func(ctx context.Context) error {
			return s.UpdatePassword(ctx, member, domain.UpdateMemberPasswordRequest{CurrentPassword: &password, NewPassword: &password})
		}},
		{"requestEmailChange", func(ctx context.Context) error {
			return s.RequestEmailChange(ctx, member, domain.UpdateMemberEmailRequest{Email: &email, CurrentPassword: &password}, 1)
		}},
		{"confirmEmailChange", func(ctx context.Context) error {
			return s.ConfirmEmailChange(ctx, domain.ConfirmMemberEmailChangeRequest{RequestId: &id, Token: &flow})
		}},
		{"requestDeletion", func(ctx context.Context) error { return s.RequestDeletion(ctx, member) }},
		{"confirmDeletion", func(ctx context.Context) error {
			return s.ConfirmDeletion(ctx, member, domain.ConfirmMemberAccountDeletionRequest{Token: &flow})
		}},
		{"requestAccountEmail", func(ctx context.Context) error { return s.RequestAccountEmail(ctx, email, true) }},
		{"confirmEmail", func(ctx context.Context) error {
			return s.ConfirmEmail(ctx, domain.ConfirmEmailRequest{UserId: &member, Token: &flow})
		}},
		{"resetPassword", func(ctx context.Context) error {
			return s.ResetPassword(ctx, domain.ResetPasswordRequest{UserId: &member, Token: &flow, NewPassword: &password})
		}},
		{"prepareEmail", func(ctx context.Context) error { _, err := s.PrepareAuthenticationEmail(ctx, id); return err }},
		{"wishlists", func(ctx context.Context) error { _, err := s.Wishlists(ctx, member, false); return err }},
		{"wishlist", func(ctx context.Context) error { _, _, err := s.Wishlist(ctx, member, id); return err }},
		{"createWishlist", func(ctx context.Context) error {
			_, _, err := s.CreateWishlist(ctx, member, domain.CreateWishlistRequest{Name: &name, Occasion: &occasion})
			return err
		}},
		{"updateWishlist", func(ctx context.Context) error {
			_, _, err := s.UpdateWishlist(ctx, member, id, domain.UpdateWishlistRequest{Name: &name, Occasion: &occasion}, 1)
			return err
		}},
		{"deleteWishlist", func(ctx context.Context) error { return s.DeleteWishlist(ctx, member, id, 1) }},
		{"archiveWishlist", func(ctx context.Context) error {
			_, _, err := s.SetWishlistArchived(ctx, member, id, true, 1)
			return err
		}},
		{"authorizeWishlist", func(ctx context.Context) error { return s.AuthorizeWishlist(ctx, member, id, true) }},
		{"wishes", func(ctx context.Context) error { _, _, err := s.Wishes(ctx, member, id); return err }},
		{"wish", func(ctx context.Context) error { _, _, err := s.Wish(ctx, member, id, id); return err }},
		{"createWish", func(ctx context.Context) error {
			_, _, err := s.CreateWish(ctx, member, id, domain.CreateWishRequest{Name: &name, Quantity: &quantity})
			return err
		}},
		{"updateWish", func(ctx context.Context) error {
			_, _, err := s.UpdateWish(ctx, member, id, id, domain.UpdateWishRequest{Name: &name, Quantity: &quantity}, 1)
			return err
		}},
		{"deleteWish", func(ctx context.Context) error { return s.DeleteWish(ctx, member, id, id, 1) }},
		{"favoriteWish", func(ctx context.Context) error {
			_, _, err := s.SetWishFavorite(ctx, member, id, id, true, 1)
			return err
		}},
		{"reorderWishes", func(ctx context.Context) error {
			_, _, err := s.ReorderWishes(ctx, member, id, []string{id}, 1)
			return err
		}},
		{"copyWish", func(ctx context.Context) error { _, _, err := s.CopyWish(ctx, member, id, id, id, "", ""); return err }},
		{"shareLink", func(ctx context.Context) error { _, _, err := s.ShareLink(ctx, member, id, "POST", nil); return err }},
		{"joinWishlist", func(ctx context.Context) error {
			_, err := s.JoinWishlist(ctx, id, flow, member, "", domain.JoinSharedWishlistRequest{})
			return err
		}},
		{"currentParticipant", func(ctx context.Context) error { _, err := s.CurrentParticipant(ctx, id, flow, member, ""); return err }},
		{"sharedWish", func(ctx context.Context) error { _, err := s.SharedWish(ctx, id, flow, id, member, ""); return err }},
		{"sharedWishlist", func(ctx context.Context) error {
			_, err := s.SharedWishlist(ctx, id, flow, member, "", false)
			return err
		}},
		{"sharedReservation", func(ctx context.Context) error {
			_, err := s.Reservation(ctx, id, flow, id, member, "", "PUT", 1, nil)
			return err
		}},
		{"ownedReservation", func(ctx context.Context) error {
			_, err := s.OwnedReservation(ctx, member, id, id, "PUT", 1, nil)
			return err
		}},
		{"reservationHistory", func(ctx context.Context) error { _, err := s.ReservationHistory(ctx, member, "", 1, 20); return err }},
		{"createSubscription", func(ctx context.Context) error { _, err := s.CreateSubscription(ctx, member, id, flow); return err }},
		{"subscription", func(ctx context.Context) error { _, err := s.Subscription(ctx, member, id); return err }},
		{"currentSubscription", func(ctx context.Context) error { _, err := s.CurrentSubscription(ctx, member, id, flow); return err }},
		{"subscriptions", func(ctx context.Context) error { _, err := s.Subscriptions(ctx, member, 1, 20); return err }},
		{"deleteSubscription", func(ctx context.Context) error { return s.DeleteSubscription(ctx, member, id) }},
		{"deleteWishImage", func(ctx context.Context) error { _, err := s.DeleteWishImage(ctx, member, id, id, 1); return err }},
		{"readProfileImage", func(ctx context.Context) error { _, _, err := s.ReadProfileImage(ctx, member, id); return err }},
		{"publicPreview", func(ctx context.Context) error { _, err := s.PublicSharePreview(ctx, id, false); return err }},
		{"requestExport", func(ctx context.Context) error { _, err := s.RequestExport(ctx, member, principal, nil); return err }},
		{"export", func(ctx context.Context) error { _, err := s.Export(ctx, member, id); return err }},
		{"twoFactorStatus", func(ctx context.Context) error { _, err := s.TwoFactorStatus(ctx, member); return err }},
		{"twoFactorSetup", func(ctx context.Context) error { _, err := s.TwoFactorSetup(ctx, flow, principal); return err }},
		{"confirmTwoFactor", func(ctx context.Context) error {
			_, err := s.ConfirmTwoFactorSetup(ctx, flow, "123456", principal)
			return err
		}},
		{"completeTwoFactor", func(ctx context.Context) error {
			_, err := s.CompleteTwoFactor(ctx, domain.TwoFactorCompletionRequest{Flow: &flow}, principal)
			return err
		}},
		{"regenerateRecoveryCodes", func(ctx context.Context) error { _, err := s.RegenerateRecoveryCodes(ctx, flow, principal); return err }},
		{"reportedWishlists", func(ctx context.Context) error { _, err := s.ReportedWishlists(ctx, "", "", nil, 1, 20); return err }},
		{"reportedWishlist", func(ctx context.Context) error { _, err := s.ReportedWishlist(ctx, id); return err }},
		{"reports", func(ctx context.Context) error { _, err := s.Reports(ctx, id, "", "", 1, 20); return err }},
		{"report", func(ctx context.Context) error { _, _, err := s.Report(ctx, id, id); return err }},
		{"reportEvents", func(ctx context.Context) error { _, err := s.ReportReviewEvents(ctx, id, id, 1, 20); return err }},
		{"moderation", func(ctx context.Context) error { _, _, err := s.Moderation(ctx, id); return err }},
		{"moderationEvents", func(ctx context.Context) error { _, err := s.ModerationEvents(ctx, id, 1, 20); return err }},
		{"auditEvents", func(ctx context.Context) error {
			_, err := s.AuditEvents(ctx, application.AuditFilter{}, 1, 20)
			return err
		}},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Act / Assert
			if err := scenario.call(ctx); !errors.Is(err, context.Canceled) {
				t.Fatalf("caller cancellation was lost: %v", err)
			}
		})
	}
	// Assert: cancellation did not create notifications or mutate the member's resources.
	var after int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM authentication_email_outbox`).Scan(&after); err != nil || before != after {
		t.Fatal("cancelled operations changed the transactional email outbox")
	}
	var wishlists []domain.WishlistResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &wishlists)
	if len(wishlists) != 0 {
		t.Fatal("cancelled mutations created business resources")
	}
	var current domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &current)
	if current.Id != member || current.DisplayName != "Test member" {
		t.Fatal("cancelled account operations changed the current identity")
	}
}
