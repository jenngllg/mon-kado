//go:build integration

package httpapi_test

import (
	"encoding/json"
	"fmt"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

type notificationPreferenceWorkflow struct {
	recipient *fixture
	trigger   func(*testing.T)
}

func TestNotificationPreferenceWorkflows_WhenEachTypeIsDisabledThenEnabled_RespectsOnlyItsSavedChoice(t *testing.T) {
	for _, kind := range []string{"wishesAdded", "groupJoined", "groupInvitation", "occasionReminder", "sharingReminder", "groupWishlistShared", "reservationUnavailable", "wishReserved", "suggestionAdded"} {
		t.Run(kind, func(t *testing.T) {
			// Arrange: each scenario uses real HTTP operations and isolated PostgreSQL.
			workflow := newNotificationPreferenceWorkflow(t, kind)
			for _, enabled := range []bool{false, true} {
				// Act: save one choice, reload it, then perform a fresh business action.
				setWorkflowPreference(t, workflow.recipient, kind, enabled)
				workflow.trigger(t)
				// Assert: muting never blocks the action, and re-enabling delivers again.
				if !enabled {
					requireNotificationAbsent(t, workflow.recipient, kind)
					continue
				}
				notification := notificationOfKind(t, workflow.recipient, kind)
				if kind == "wishesAdded" || kind == "occasionReminder" {
					requireStatus(t, workflow.recipient.send(t, "GET", "/api/v1/notifications/"+notification.Id+"/destination", "", nil), 200, nil)
				}
				if notification.Href == "" {
					t.Fatal("notification did not retain its destination")
				}
				requireStatus(t, workflow.recipient.send(t, "POST", "/api/v1/notifications/"+notification.Id+"/read", "", nil), 204, nil)
				if notificationOfKind(t, workflow.recipient, kind).ReadAt == nil {
					t.Fatal("notification read state did not persist")
				}
				setWorkflowPreference(t, workflow.recipient, kind, false)
				retained := notificationOfKind(t, workflow.recipient, kind)
				if retained.Id != notification.Id || retained.ReadAt == nil {
					t.Fatal("disabling again changed received notification history")
				}
			}
		})
	}
}

func setWorkflowPreference(t *testing.T, f *fixture, kind string, enabled bool) {
	t.Helper()
	const path = "/api/v1/members/current/notification-preferences"
	choices := map[string]bool{"wishesAdded": true, "groupJoined": true, "groupInvitation": true, "occasionReminder": true, "sharingReminder": true, "groupWishlistShared": true, "reservationUnavailable": true, "wishReserved": true, "suggestionAdded": true}
	choices[kind] = enabled
	read := f.send(t, "GET", path, "", nil)
	tag := read.Header.Get("ETag")
	requireStatus(t, read, 200, nil)
	data, err := json.Marshal(choices)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "PUT", path, string(data), map[string]string{"If-Match": tag}), 200, nil)
	var saved map[string]bool
	requireStatus(t, f.send(t, "GET", path, "", nil), 200, &saved)
	if len(saved) != len(choices) {
		t.Fatal("incomplete persisted preferences")
	}
	for key, wanted := range choices {
		if saved[key] != wanted {
			t.Fatalf("changing %s altered saved choice %s", kind, key)
		}
	}
}

func newNotificationPreferenceWorkflow(t *testing.T, kind string) notificationPreferenceWorkflow {
	t.Helper()
	switch kind {
	case "wishesAdded", "occasionReminder":
		s := newSubscriptionScenario(t)
		requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
		return notificationPreferenceWorkflow{s.member, func(t *testing.T) {
			if kind == "wishesAdded" {
				requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Workflow wish"}`, nil), 201, nil)
				return
			}
			previous := s.owner.service.Now
			s.owner.service.Now = func() time.Time { return time.Date(2027, 1, 11, 12, 0, 0, 0, time.UTC) }
			defer func() { s.owner.service.Now = previous }()
			if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
				t.Fatal(err)
			}
		}}
	case "sharingReminder":
		f := newFixture(t)
		f.member(t, "workflow-sharing@example.test")
		var list domain.WishlistResponse
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Unshared workflow list","occasion":"other"}`, nil), 201, &list)
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Ready to share"}`, nil), 201, nil)
		return notificationPreferenceWorkflow{f, func(t *testing.T) {
			previous := f.service.Now
			f.service.Now = func() time.Time { return list.CreatedAt.Add(48 * time.Hour) }
			defer func() { f.service.Now = previous }()
			if err := f.service.GenerateNotificationReminders(t.Context()); err != nil {
				t.Fatal(err)
			}
		}}
	case "groupInvitation":
		s := newGroupScenario(t)
		var pending string
		return notificationPreferenceWorkflow{s.member, func(t *testing.T) {
			if pending != "" {
				requireStatus(t, s.owner.send(t, "DELETE", s.base+"/invitations/"+pending, "", nil), 204, nil)
			}
			invitation := s.invite(t, s.memberID)
			pending = invitation.Id
			requireStatus(t, s.member.send(t, "GET", "/api/v1/group-invitations/"+pending, "", nil), 200, nil)
		}}
	case "groupJoined":
		s := newGroupScenario(t)
		joined := false
		return notificationPreferenceWorkflow{s.owner, func(t *testing.T) {
			if joined {
				s.accept(t, s.foreign, s.foreignID)
				return
			}
			s.accept(t, s.member, s.memberID)
			joined = true
		}}
	case "groupWishlistShared":
		s := newGroupScenario(t)
		s.accept(t, s.member, s.memberID)
		sequence := 0
		return notificationPreferenceWorkflow{s.member, func(t *testing.T) {
			sequence++
			var list domain.WishlistResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", fmt.Sprintf(`{"name":"Workflow shared list %d","occasion":"other"}`, sequence), nil), 201, &list)
			shared := s.base + "/wishlists/" + list.Id
			requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
			requireStatus(t, s.member.send(t, "GET", shared, "", nil), 200, nil)
			// Muting shared-list notices must not mute followed-list wish additions.
			requireStatus(t, s.member.send(t, "POST", shared+"/subscriptions", "", nil), 201, nil)
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Independent wish notification"}`, nil), 201, nil)
			notificationOfKind(t, s.member, "wishesAdded")
		}}
	case "suggestionAdded":
		s := newGroupScenario(t)
		s.accept(t, s.member, s.memberID)
		s.accept(t, s.foreign, s.foreignID)
		list := groupVisibilityList(t, s)
		shared := s.base + "/wishlists/" + list.Id
		requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
		return notificationPreferenceWorkflow{s.foreign, func(t *testing.T) {
			requireStatus(t, s.member.send(t, "POST", shared+"/suggestions", `{"name":"Workflow surprise idea"}`, nil), 201, nil)
			requireNotificationAbsent(t, s.owner, "suggestionAdded")
			requireNotificationAbsent(t, s.member, "suggestionAdded")
		}}
	case "wishReserved", "reservationUnavailable":
		s := newGroupScenario(t)
		s.accept(t, s.member, s.memberID)
		var list domain.WishlistResponse
		requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Workflow reservations","occasion":"other","surpriseMode":false}`, nil), 201, &list)
		own := "/api/v1/wishlists/" + list.Id
		shared := s.base + "/wishlists/" + list.Id
		requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
		recipient := s.owner
		if kind == "reservationUnavailable" {
			recipient = s.member
		}
		return notificationPreferenceWorkflow{recipient, func(t *testing.T) {
			var wish domain.WishResponse
			created := s.owner.send(t, "POST", own+"/wishes", `{"name":"Workflow reserved wish"}`, nil)
			tag := created.Header.Get("ETag")
			requireStatus(t, created, 201, &wish)
			reservation := shared + "/wishes/" + wish.Id + "/reservations/current"
			requireStatus(t, s.member.send(t, "PUT", reservation, `{"quantity":1}`, nil), 201, nil)
			requireStatus(t, s.member.send(t, "GET", reservation, "", nil), 200, nil)
			if kind == "reservationUnavailable" {
				requireStatus(t, s.owner.send(t, "DELETE", own+"/wishes/"+wish.Id, "", map[string]string{"If-Match": tag}), 204, nil)
			}
		}}
	default:
		t.Fatalf("workflow not defined for %s", kind)
		return notificationPreferenceWorkflow{}
	}
}
