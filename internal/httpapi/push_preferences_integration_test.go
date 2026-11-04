//go:build integration

package httpapi_test

import (
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/domain"
	"testing"
)

const pushPreferencePath = "/api/v1/members/current/push-preferences"

func pushChoices() map[string]bool {
	return map[string]bool{"wishesAdded": true, "groupJoined": true, "groupInvitation": true, "occasionReminder": true, "sharingReminder": true, "groupWishlistShared": true, "reservationUnavailable": true, "wishReserved": true, "suggestionAdded": true}
}
func savePushChoices(t *testing.T, f *fixture, choices map[string]bool) {
	t.Helper()
	read := f.send(t, "GET", pushPreferencePath, "", nil)
	tag := read.Header.Get("ETag")
	requireStatus(t, read, 200, nil)
	data, _ := json.Marshal(choices)
	requireStatus(t, f.send(t, "PUT", pushPreferencePath, string(data), map[string]string{"If-Match": tag}), 200, nil)
}
func TestPushPreferences_WhenUnconfigured_DefaultsEveryTypeOnWithoutCreatingASubscription(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "push-preferences@example.test")
	// Act
	var choices map[string]bool
	requireStatus(t, f.send(t, "GET", pushPreferencePath, "", nil), 200, &choices)
	// Assert
	if len(choices) != 9 {
		t.Fatal("incomplete default choices")
	}
	for _, enabled := range choices {
		if !enabled {
			t.Fatal("push notifications are not enabled by default")
		}
	}
	var devices []domain.PushSubscriptionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/push-subscriptions", "", nil), 200, &devices)
	if len(devices) != 0 {
		t.Fatal("reading default preferences registered a browser")
	}
}
func TestPushPreferences_WhenDisabledWithoutBrowserPermission_PersistsIndependentlyOfInApp(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "push-optout@example.test")
	choices := pushChoices()
	for kind := range choices {
		choices[kind] = false
	}
	// Act
	savePushChoices(t, f, choices)
	// Assert
	var saved map[string]bool
	requireStatus(t, f.send(t, "GET", pushPreferencePath, "", nil), 200, &saved)
	for _, enabled := range saved {
		if enabled {
			t.Fatal("push opt-out did not persist")
		}
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/notification-preferences", "", nil), 200, &saved)
	for _, enabled := range saved {
		if !enabled {
			t.Fatal("push opt-out changed in-app settings")
		}
	}
	data, _ := json.Marshal(choices)
	requireStatus(t, f.send(t, "PUT", pushPreferencePath, string(data), nil), 428, nil)
	requireStatus(t, f.send(t, "PUT", pushPreferencePath, string(data), map[string]string{"If-Match": `"00000000"`}), 412, nil)
}
func TestPushPreferences_WhenSaved_AppliesOneChannelAcrossSubscriptionsAndChecksItBeforeDelivery(t *testing.T) {
	for _, enabled := range []bool{false, true} {
		t.Run(map[bool]string{true: "enabled", false: "disabled"}[enabled], func(t *testing.T) {
			// Arrange
			s := newSubscriptionScenario(t)
			requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
			first := activatePush(t, s.member, "https://fcm.googleapis.com/global-one")
			activatePush(t, s.member, "https://fcm.googleapis.com/global-two")
			savePush(t, s.member, first, []string{"wishesAdded"})
			savePushChoices(t, s.member, pushChoices())
			requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Channel test"}`, nil), 201, nil)
			choices := pushChoices()
			choices["wishesAdded"] = enabled
			savePushChoices(t, s.member, choices)
			// Act / Assert: the channel supersedes historical device choices on every subscription.
			sender := &pushFake{status: 201}
			if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
				t.Fatal(err)
			}
			// Both subscriptions were queued by the shared preference, superseding legacy device flags.
			expected := 0
			if enabled {
				expected = 2
			}
			if sender.calls != expected {
				t.Fatalf("calls=%d expected=%d", sender.calls, expected)
			}
		})
	}
}
