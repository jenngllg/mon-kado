//go:build integration

package httpapi_test

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/worker"
)

const preferencePath = "/api/v1/members/current/notification-preferences"

func preferenceChoices(enabled bool) map[string]bool {
	return map[string]bool{"wishesAdded": enabled, "groupJoined": enabled, "groupInvitation": enabled, "occasionReminder": enabled, "sharingReminder": enabled, "groupWishlistShared": enabled, "reservationUnavailable": enabled, "wishReserved": enabled, "suggestionAdded": enabled}
}

func preferenceBody(t *testing.T, value any) string {
	t.Helper()
	data, err := json.Marshal(value)
	if err != nil {
		t.Fatal(err)
	}
	return string(data)
}

func readPreferences(t *testing.T, f *fixture) (map[string]bool, string) {
	t.Helper()
	r := f.send(t, "GET", preferencePath, "", nil)
	tag := r.Header.Get("ETag")
	var value map[string]bool
	requireStatus(t, r, 200, &value)
	if len(value) != 9 || tag == "" {
		t.Fatal("incomplete preference response")
	}
	return value, tag
}

func savePreferences(t *testing.T, f *fixture, value map[string]bool) {
	t.Helper()
	_, tag := readPreferences(t, f)
	requireStatus(t, f.send(t, "PUT", preferencePath, preferenceBody(t, value), map[string]string{"If-Match": tag}), 200, nil)
}

func TestNotificationPreferences_WhenSaved_PersistsAllTypesAndKeepsMembersIsolated(t *testing.T) {
	// Arrange
	f := newFixture(t)
	anonymous := f.visitor(t)
	requireStatus(t, anonymous.send(t, "GET", preferencePath, "", nil), 401, nil)
	requireStatus(t, anonymous.send(t, "PUT", preferencePath, preferenceBody(t, preferenceChoices(false)), nil), 401, nil)
	member := f.member(t, "notification-settings@example.test")
	defaults, tag := readPreferences(t, f)
	for _, enabled := range defaults {
		if !enabled {
			t.Fatal("notifications must default to enabled")
		}
	}
	if tag != `"00000000"` {
		t.Fatal("unexpected default version")
	}
	var count int
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM member_notification_preferences WHERE member_id=$1`, member).Scan(&count); err != nil || count != 0 {
		t.Fatal("GET mutated persistent settings")
	}
	// Act / Assert
	for _, enabled := range []bool{false, true} {
		wanted := preferenceChoices(enabled)
		r := f.send(t, "PUT", preferencePath, preferenceBody(t, wanted), map[string]string{"If-Match": tag})
		next := r.Header.Get("ETag")
		var saved map[string]bool
		requireStatus(t, r, 200, &saved)
		for key, value := range saved {
			if value != wanted[key] {
				t.Fatalf("choice %s was lost", key)
			}
		}
		if next == tag {
			t.Fatal("saved settings retained a stale version")
		}
		requireStatus(t, f.send(t, "PUT", preferencePath, preferenceBody(t, wanted), map[string]string{"If-Match": tag}), 412, nil)
		reloaded, current := readPreferences(t, f)
		for key, value := range reloaded {
			if value != wanted[key] {
				t.Fatalf("choice %s did not persist", key)
			}
		}
		if current != next {
			t.Fatal("GET and PUT version differ")
		}
		tag = next
	}
	other := f.visitor(t)
	other.member(t, "other-notification-settings@example.test")
	otherChoices, otherTag := readPreferences(t, other)
	if otherTag != `"00000000"` {
		t.Fatal("settings leaked across members")
	}
	for _, enabled := range otherChoices {
		if !enabled {
			t.Fatal("foreign disabled preferences leaked")
		}
	}
	requireStatus(t, other.send(t, "GET", "/api/v1/members/"+member+"/notification-preferences", "", nil), 404, nil)
}

func TestNotificationPreferences_WhenInputIsIncompleteOrUnprotected_RejectsBeforeSaving(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "invalid-notification-settings@example.test")
	_, tag := readPreferences(t, f)
	valid := preferenceBody(t, preferenceChoices(false))
	// Act / Assert
	requireStatus(t, f.send(t, "PUT", preferencePath, valid, nil), 428, nil)
	requireStatus(t, f.send(t, "PUT", preferencePath, valid, map[string]string{"If-Match": "invalid"}), 400, nil)
	for _, body := range []string{`{}`, `null`, `{"wishesAdded":false}`, `{"wishesAdded":null}`, `{"wishesAdded":1}`, `{"wishesAdded":"false"}`, `{"unknown":true}`} {
		t.Run(body, func(t *testing.T) {
			requireStatus(t, f.send(t, "PUT", preferencePath, body, map[string]string{"If-Match": tag}), 400, nil)
		})
	}
	csrf := f.csrf
	f.csrf = ""
	requireStatus(t, f.send(t, "PUT", preferencePath, valid, map[string]string{"If-Match": tag}), 400, nil)
	f.csrf = csrf
	_, after := readPreferences(t, f)
	if after != tag {
		t.Fatal("invalid input changed settings")
	}
}

func TestNotificationPreferences_WhenFirstSavesRace_OnlyOneConditionalWriteSucceeds(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "concurrent-notification-settings@example.test")
	_, tag := readPreferences(t, f)
	requests := []*http.Request{}
	for _, enabled := range []bool{false, true} {
		r, err := http.NewRequestWithContext(t.Context(), "PUT", f.server.URL+preferencePath, bytes.NewBufferString(preferenceBody(t, preferenceChoices(enabled))))
		if err != nil {
			t.Fatal(err)
		}
		r.Header.Set("Content-Type", "application/json")
		r.Header.Set("Authorization", "Bearer "+f.access)
		r.Header.Set("X-CSRF-TOKEN", f.csrf)
		r.Header.Set("If-Match", tag)
		requests = append(requests, r)
	}
	start := make(chan struct{})
	type result struct {
		index, status int
		err           error
	}
	results := make(chan result, 2)
	// Act
	for index, request := range requests {
		go func() {
			<-start
			r, err := f.client.Do(request)
			status := 0
			if err == nil {
				status = r.StatusCode
				r.Body.Close()
			}
			results <- result{index, status, err}
		}()
	}
	close(start)
	winner := -1
	conflicts := 0
	for range 2 {
		r := <-results
		if r.err != nil {
			t.Fatal("concurrent request failed")
		}
		switch r.status {
		case 200:
			winner = r.index
		case 412:
			conflicts++
		default:
			t.Fatalf("unexpected status %d", r.status)
		}
	}
	// Assert
	if winner < 0 || conflicts != 1 {
		t.Fatal("concurrent first saves were not protected")
	}
	saved, _ := readPreferences(t, f)
	for _, enabled := range saved {
		if enabled != (winner == 1) {
			t.Fatal("concurrent state was mixed")
		}
	}
}

func TestNotificationPreferences_WhenWishUpdatesAreDisabled_PreservesHistoryWithoutGroupingNewActivity(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"First"}`, nil), 201, nil)
	before := notificationOfKind(t, s.member, "wishesAdded")
	choices := preferenceChoices(true)
	choices["wishesAdded"] = false
	savePreferences(t, s.member, choices)
	// Act
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Muted"}`, nil), 201, nil)
	// Assert
	after := notificationOfKind(t, s.member, "wishesAdded")
	if after.Id != before.Id || after.Quantity != 1 {
		t.Fatal("disabled activity changed existing history")
	}
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/"+before.Id+"/read", "", nil), 204, nil)
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Still muted"}`, nil), 201, nil)
	if len(notificationPage(t, s.member)) != 1 {
		t.Fatal("new disabled activity was inserted")
	}
	choices["wishesAdded"] = true
	savePreferences(t, s.member, choices)
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Enabled again"}`, nil), 201, nil)
	if len(notificationPage(t, s.member)) != 2 {
		t.Fatal("enabled activity was not delivered")
	}
}

func TestNotificationPreferences_WhenGroupTypesAreDisabled_ControlsEachRecipientWithoutChangingTheActions(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	savePreferences(t, s.owner, preferenceChoices(false))
	savePreferences(t, s.member, preferenceChoices(false))
	savePreferences(t, s.foreign, preferenceChoices(false))
	// Act
	invitation := s.invite(t, s.memberID)
	requireNotificationAbsent(t, s.member, "groupInvitation")
	requireStatus(t, s.member.send(t, "POST", "/api/v1/group-invitations/"+invitation.Id+"/accept", "", nil), 200, nil)
	s.accept(t, s.foreign, s.foreignID)
	list := groupVisibilityList(t, s)
	shared := s.base + "/wishlists/" + list.Id
	requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
	requireStatus(t, s.member.send(t, "POST", shared+"/suggestions", `{"name":"Muted suggestion"}`, nil), 201, nil)
	// Assert: invitation, join, shared list and suggestion are all optional.
	for _, f := range []*fixture{s.owner, s.member, s.foreign} {
		if len(notificationPage(t, f)) != 0 {
			t.Fatal("disabled group notification was delivered")
		}
	}
	choices := preferenceChoices(false)
	choices["suggestionAdded"] = true
	savePreferences(t, s.foreign, choices)
	requireStatus(t, s.member.send(t, "POST", shared+"/suggestions", `{"name":"Enabled suggestion"}`, nil), 201, nil)
	notificationOfKind(t, s.foreign, "suggestionAdded")
	requireNotificationAbsent(t, s.owner, "suggestionAdded")
}

func TestNotificationPreferences_WhenReservationTypesAreDisabled_LeavesReservationsAndPrivacyIntact(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Preference reservation","occasion":"other","surpriseMode":false}`, nil), 201, &list)
	own := "/api/v1/wishlists/" + list.Id
	shared := s.base + "/wishlists/" + list.Id
	var wish domain.WishResponse
	created := s.owner.send(t, "POST", own+"/wishes", `{"name":"Reserved wish"}`, nil)
	tag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
	ownerChoices := preferenceChoices(true)
	ownerChoices["wishReserved"] = false
	savePreferences(t, s.owner, ownerChoices)
	memberChoices := preferenceChoices(true)
	memberChoices["reservationUnavailable"] = false
	savePreferences(t, s.member, memberChoices)
	// Act / Assert
	requireStatus(t, s.member.send(t, "PUT", shared+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
	requireNotificationAbsent(t, s.owner, "wishReserved")
	requireStatus(t, s.owner.send(t, "DELETE", own+"/wishes/"+wish.Id, "", map[string]string{"If-Match": tag}), 204, nil)
	requireNotificationAbsent(t, s.member, "reservationUnavailable")
}

func TestNotificationPreferences_WhenReminderBatchContainsDisabledMembers_DoesNotStarveEnabledMembers(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	choices := preferenceChoices(true)
	choices["occasionReminder"] = false
	savePreferences(t, s.member, choices)
	clock := s.owner.service.Now
	t.Cleanup(func() { s.owner.service.Now = clock })
	s.owner.service.Now = func() time.Time { return time.Date(2027, 1, 11, 12, 0, 0, 0, time.UTC) }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = clock
	requireNotificationAbsent(t, s.member, "occasionReminder")
	choices["occasionReminder"] = true
	savePreferences(t, s.member, choices)
	s.owner.service.Now = func() time.Time { return time.Date(2027, 1, 11, 12, 0, 0, 0, time.UTC) }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = clock
	notificationOfKind(t, s.member, "occasionReminder")
	// First candidate is disabled; a batch of one must still reach the next owner.
	s.owner.service.Config.JobOptions.BatchSize = 1
	for index, f := range []*fixture{s.owner, s.member} {
		var list domain.WishlistResponse
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", fmt.Sprintf(`{"name":"Unshared %d","occasion":"other"}`, index), nil), 201, &list)
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Ready"}`, nil), 201, nil)
	}
	choices = preferenceChoices(true)
	choices["sharingReminder"] = false
	savePreferences(t, s.owner, choices)
	s.owner.service.Now = func() time.Time { return clock().Add(49 * time.Hour) }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = clock
	requireNotificationAbsent(t, s.owner, "sharingReminder")
	notificationOfKind(t, s.member, "sharingReminder")
}

func TestNotificationPreferences_WhenExporting_IncludesOnlyTheMembersChoices(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "export-notification-settings@example.test")
	choices := preferenceChoices(true)
	choices["wishReserved"] = false
	savePreferences(t, f, choices)
	var export domain.PersonalDataExportResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 202, &export)
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	// Act
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	download := f.send(t, "GET", "/api/v1/members/current/data-exports/"+export.Id+"/archive", "", nil)
	content, err := io.ReadAll(download.Body)
	download.Body.Close()
	if err != nil || download.StatusCode != 200 {
		t.Fatal("private export is unavailable")
	}
	var data struct {
		AccountActivity struct {
			NotificationPreferences []struct {
				DisabledKinds []string `json:"disabledKinds"`
			} `json:"notificationPreferences"`
		} `json:"accountActivity"`
	}
	if err = json.Unmarshal(notificationExportData(t, content), &data); err != nil {
		t.Fatal(err)
	}
	// Assert
	if len(data.AccountActivity.NotificationPreferences) != 1 || len(data.AccountActivity.NotificationPreferences[0].DisabledKinds) != 1 || data.AccountActivity.NotificationPreferences[0].DisabledKinds[0] != "wishReserved" {
		t.Fatal("the export lost notification choices")
	}
}

func TestNotificationPreferences_WhenImpersonating_ChangesOnlyTheEffectiveMembersChoices(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "target-notification-settings@example.test")
	targetAccess := f.access
	var target domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &target)
	f.administrator(t, "admin-notification-settings@example.test")
	adminAccess := f.access
	var simulation application.ImpersonationResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/admin/members/"+target.Id+"/impersonations", "", nil), 201, &simulation)
	headers := map[string]string{"X-MonKado-Impersonation": simulation.ID}
	current := f.send(t, "GET", preferencePath, "", headers)
	headers["If-Match"] = current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	// Act
	requireStatus(t, f.send(t, "PUT", preferencePath, preferenceBody(t, preferenceChoices(false)), headers), 200, nil)
	// Assert
	f.access = targetAccess
	saved, _ := readPreferences(t, f)
	for _, enabled := range saved {
		if enabled {
			t.Fatal("delegated choices were not saved for the target")
		}
	}
	f.access = adminAccess
	saved, _ = readPreferences(t, f)
	for _, enabled := range saved {
		if !enabled {
			t.Fatal("delegation changed the administrator's preferences")
		}
	}
}
