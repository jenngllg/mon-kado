//go:build integration

package httpapi_test

import (
	"context"
	"encoding/json"
	"fmt"
	"github.com/JennGllg/mon-kado/internal/domain"
	"slices"
	"strings"
	"testing"
	"time"
)

type pushFake struct {
	calls  int
	status int
	err    error
}

func (p *pushFake) Send(ctx context.Context, endpoint string) (int, error) {
	p.calls++
	if ctx.Err() != nil {
		return 0, ctx.Err()
	}
	return p.status, p.err
}
func activatePush(t *testing.T, f *fixture, endpoint string) domain.PushSubscriptionResponse {
	t.Helper()
	f.service.Config.PushOptions.Enabled = true
	var d domain.PushSubscriptionResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/push-subscriptions", fmt.Sprintf(`{"name":"Test phone","endpoint":%q}`, endpoint), nil), 201, &d)
	return d
}
func savePush(t *testing.T, f *fixture, d domain.PushSubscriptionResponse, disabled []string) domain.PushSubscriptionResponse {
	t.Helper()
	data, _ := json.Marshal(domain.UpdatePushSubscriptionRequest{Name: d.Name, DisabledKinds: disabled})
	var saved domain.PushSubscriptionResponse
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/push-subscriptions/"+d.Id, string(data), map[string]string{"If-Match": d.Version}), 200, &saved)
	return saved
}
func TestPush_WhenDeviceIsRegistered_OwnershipPersistenceAndConcurrencyAreEnforced(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	d := activatePush(t, s.member, "https://fcm.googleapis.com/device-one")
	// Act / Assert: reloading reveals only private metadata and preserves the device settings.
	var devices []domain.PushSubscriptionResponse
	requireStatus(t, s.member.send(t, "GET", "/api/v1/members/current/push-subscriptions", "", nil), 200, &devices)
	if len(devices) != 1 || devices[0].Id != d.Id || len(d.EndpointHash) != 64 || d.Version == "" {
		t.Fatal("device was not persisted")
	}
	requireStatus(t, s.owner.send(t, "DELETE", "/api/v1/members/current/push-subscriptions/"+d.Id, "", nil), 404, nil)
	d = savePush(t, s.member, d, []string{"wishesAdded"})
	requireStatus(t, s.member.send(t, "PUT", "/api/v1/members/current/push-subscriptions/"+d.Id, `{"name":"Phone","disabledKinds":[]}`, map[string]string{"If-Match": `"00000000"`}), 412, nil)
	requireStatus(t, s.member.send(t, "PUT", "/api/v1/members/current/push-subscriptions/"+d.Id, `{"name":"Phone","disabledKinds":[]}`, nil), 428, nil)
	requireStatus(t, s.member.send(t, "POST", "/api/v1/members/current/push-subscriptions", `{"name":"Phone","endpoint":"https://localhost/private"}`, nil), 400, nil)
	requireStatus(t, s.member.send(t, "DELETE", "/api/v1/members/current/push-subscriptions/"+d.Id, "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "DELETE", "/api/v1/members/current/push-subscriptions/"+d.Id, "", nil), 404, nil)
}
func TestPush_WhenInAppIsMuted_DeliversIndependentlyOnlyOnceToEnabledDevices(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	activatePush(t, s.member, "https://fcm.googleapis.com/device-one")
	muted := activatePush(t, s.member, "https://fcm.googleapis.com/device-two")
	savePush(t, s.member, muted, []string{"wishesAdded"})
	setWorkflowPreference(t, s.member, "wishesAdded", false)
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Private wish"}`, nil), 201, nil)
	// Act
	sender := &pushFake{status: 201}
	if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
		t.Fatal(err)
	}
	if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
		t.Fatal(err)
	}
	// Assert
	if sender.calls != 1 {
		t.Fatalf("received %d calls", sender.calls)
	}
	requireNotificationAbsent(t, s.member, "wishesAdded")
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Grouped wish"}`, nil), 201, nil)
	if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
		t.Fatal(err)
	}
	if sender.calls != 1 {
		t.Fatal("grouping re-alerted")
	}
}
func TestPush_WhenAuthorizationOrPreferenceChangesBeforeDelivery_DoesNotSend(t *testing.T) {
	for _, scenario := range []string{"unfollow", "deviceMuted", "deviceRemoved", "read", "sessionRevoked"} {
		t.Run(scenario, func(t *testing.T) {
			// Arrange
			s := newSubscriptionScenario(t)
			var follow domain.WishlistSubscriptionResponse
			requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, &follow)
			d := activatePush(t, s.member, "https://fcm.googleapis.com/device")
			requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Wish"}`, nil), 201, nil)
			switch scenario {
			case "unfollow":
				requireStatus(t, s.member.send(t, "DELETE", "/api/v1/wishlist-subscriptions/"+follow.Id, "", nil), 204, nil)
			case "deviceMuted":
				savePush(t, s.member, d, []string{"wishesAdded"})
			case "deviceRemoved":
				requireStatus(t, s.member.send(t, "DELETE", "/api/v1/members/current/push-subscriptions/"+d.Id, "", nil), 204, nil)
			case "read":
				requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/read", "", nil), 204, nil)
			case "sessionRevoked":
				if _, err := s.member.store.Pool.Exec(t.Context(), `UPDATE authentication_sessions SET revoked_at=$1 WHERE user_id=(SELECT member_id FROM member_push_subscriptions WHERE id=$2)`, s.member.service.Now(), d.Id); err != nil {
					t.Fatal(err)
				}
			}
			// Act / Assert
			sender := &pushFake{status: 201}
			if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
				t.Fatal(err)
			}
			if sender.calls != 0 {
				t.Fatal("unauthorized delivery")
			}
		})
	}
}
func TestPush_WhenProviderRejectsOrTemporarilyFails_UsesBoundedOutboxWithoutAmbiguousReplay(t *testing.T) {
	for _, status := range []int{201, 410, 429, 500, 400, 0} {
		t.Run(fmt.Sprint(status), func(t *testing.T) {
			// Arrange
			s := newSubscriptionScenario(t)
			requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
			activatePush(t, s.member, "https://fcm.googleapis.com/device")
			now := time.Now().UTC()
			s.member.service.Now = func() time.Time { return now }
			requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Wish"}`, nil), 201, nil)
			now = now.Add(time.Second)
			sender := &pushFake{status: status}
			if status == 0 {
				sender.err = fmt.Errorf("uncertain network outcome")
			}
			// Act
			if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
				t.Fatal(err)
			}
			if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
				t.Fatal(err)
			}
			// Assert: no immediate retries; only explicit transient responses schedule another attempt.
			if sender.calls != 1 {
				t.Fatal("unexpected repeated attempt")
			}
			now = now.Add(2 * time.Minute)
			sender.status = 201
			if err := s.member.service.DeliverPush(t.Context(), sender); err != nil {
				t.Fatal(err)
			}
			expected := 1
			if status == 429 || status == 500 {
				expected = 2
			}
			if sender.calls != expected {
				t.Fatalf("calls=%d expected=%d", sender.calls, expected)
			}
		})
	}
}

func TestPush_WhenOwnerEnablesSurpriseBeforeDelivery_CancelsReservationSignal(t *testing.T) {
	// Arrange
	workflow := newNotificationPreferenceWorkflow(t, "wishReserved")
	activatePush(t, workflow.recipient, "https://fcm.googleapis.com/surprise-device")
	workflow.trigger(t)
	event := notificationOfKind(t, workflow.recipient, "wishReserved")
	path := "/api/v1/wishlists/" + strings.TrimPrefix(event.Href, "/lists/")
	read := workflow.recipient.send(t, "GET", path, "", nil)
	tag := read.Header.Get("ETag")
	requireStatus(t, read, 200, nil)
	requireStatus(t, workflow.recipient.send(t, "PUT", path, `{"name":"Workflow reservations","occasion":"other","surpriseMode":true}`, map[string]string{"If-Match": tag}), 200, nil)
	// Act / Assert
	sender := &pushFake{status: 201}
	if err := workflow.recipient.service.DeliverPush(t.Context(), sender); err != nil {
		t.Fatal(err)
	}
	if sender.calls != 0 {
		t.Fatal("surprise reservation signalled to owner")
	}
}

func TestPush_WhenInitiallyEnablingOneType_DoesNotEnableOtherTypes(t *testing.T) {
	// Arrange
	scenario := newSubscriptionScenario(t)
	scenario.member.service.Config.PushOptions.Enabled = true
	choices := []string{"wishesAdded", "groupJoined", "groupInvitation", "occasionReminder", "sharingReminder", "groupWishlistShared", "reservationUnavailable", "wishReserved"}
	data, _ := json.Marshal(map[string]any{"endpoint": "https://fcm.googleapis.com/initial-choice", "name": "Android", "disabledKinds": choices})
	// Act
	var created domain.PushSubscriptionResponse
	requireStatus(t, scenario.member.send(t, "POST", "/api/v1/members/current/push-subscriptions", string(data), nil), 201, &created)
	// Assert: the first subscription is already restricted when its transaction commits.
	if !slices.Equal(created.DisabledKinds, choices) {
		t.Fatal("initial push choices were not applied atomically")
	}
	var reloaded domain.PushSubscriptionResponse
	requireStatus(t, scenario.member.send(t, "GET", "/api/v1/members/current/push-subscriptions/"+created.Id, "", nil), 200, &reloaded)
	if !slices.Equal(reloaded.DisabledKinds, choices) {
		t.Fatal("initial push choices were not persisted")
	}

	// An older registration client without choices cannot silently reset them.
	var registered domain.PushSubscriptionResponse
	requireStatus(t, scenario.member.send(t, "POST", "/api/v1/members/current/push-subscriptions", `{"endpoint":"https://fcm.googleapis.com/initial-choice","name":"Automatic label"}`, nil), 201, &registered)
	if !slices.Equal(registered.DisabledKinds, choices) {
		t.Fatal("registration without choices reset preferences")
	}
	// Explicit first-save choices apply atomically when rebinding the same browser.
	requireStatus(t, scenario.member.send(t, "POST", "/api/v1/members/current/push-subscriptions", `{"endpoint":"https://fcm.googleapis.com/initial-choice","name":"Automatic label","disabledKinds":[]}`, nil), 201, &registered)
	if len(registered.DisabledKinds) != 0 {
		t.Fatal("explicit initial choices were ignored on reactivation")
	}
	requireStatus(t, scenario.member.send(t, "POST", "/api/v1/members/current/push-subscriptions", `{"endpoint":"https://fcm.googleapis.com/invalid","name":"Android","disabledKinds":["invalid"]}`, nil), 400, nil)
}
