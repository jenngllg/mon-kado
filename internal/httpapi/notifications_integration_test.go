//go:build integration

package httpapi_test

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func notificationPage(t *testing.T, f *fixture) []domain.NotificationResponse {
	t.Helper()
	var page domain.PaginatedResponse[domain.NotificationResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/notifications", "", nil), 200, &page)
	return page.Items
}
func notificationOfKind(t *testing.T, f *fixture, kind string) domain.NotificationResponse {
	t.Helper()
	for _, n := range notificationPage(t, f) {
		if n.Kind == kind {
			return n
		}
	}
	t.Fatalf("missing %s notification", kind)
	return domain.NotificationResponse{}
}
func requireNotificationAbsent(t *testing.T, f *fixture, kind string) {
	t.Helper()
	for _, n := range notificationPage(t, f) {
		if n.Kind == kind {
			t.Fatalf("unexpected %s notification", kind)
		}
	}
}

func TestNotifications_WhenFollowing_AddsGroupsAndPersistsReadStateWithoutLeakingCapabilities(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	var follow domain.WishlistSubscriptionResponse
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, &follow)
	// Existing wishes and following itself must not produce notifications.
	if len(notificationPage(t, s.member)) != 0 {
		t.Fatal("following generated historical activity")
	}
	for _, name := range []string{"First", "Second"} {
		requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", fmt.Sprintf(`{"name":%q}`, name), nil), 201, nil)
	}
	// Act / Assert
	n := notificationOfKind(t, s.member, "wishesAdded")
	if n.Quantity != 2 || n.ReadAt != nil || strings.Contains(n.Href, "#") || n.WishlistName == nil || *n.WishlistName != s.list.Name {
		t.Fatal("incorrect grouped notification")
	}
	var status domain.NotificationStatusResponse
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications/status", "", nil), 200, &status)
	if !status.HasUnread {
		t.Fatal("missing unread badge")
	}
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/notifications/"+n.Id+"/read", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/"+n.Id+"/read", "", nil), 204, nil)
	n = notificationOfKind(t, s.member, "wishesAdded")
	if n.ReadAt == nil {
		t.Fatal("read state was not persisted")
	}
	var destination struct {
		Href string `json:"href"`
	}
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications/"+n.Id+"/destination", "", nil), 200, &destination)
	if destination.Href != s.link.ShareUrl {
		t.Fatal("notification did not resolve its followed list")
	}
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Third"}`, nil), 201, nil)
	items := notificationPage(t, s.member)
	if len(items) != 2 {
		t.Fatal("new activity reused a read notification")
	}
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/read", "", nil), 204, nil)
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications/status", "", nil), 200, &status)
	if status.HasUnread {
		t.Fatal("read all left unread activity")
	}
	requireStatus(t, s.member.send(t, "DELETE", "/api/v1/wishlist-subscriptions/"+follow.Id, "", nil), 204, nil)
	if len(notificationPage(t, s.member)) != 0 {
		t.Fatal("unfollow retained private activity")
	}
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications/"+n.Id+"/destination", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications?pageSize=0", "", nil), 400, nil)
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/invalid/read", "", nil), 404, nil)
	old := s.member.csrf
	s.member.csrf = ""
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/read", "", nil), 400, nil)
	s.member.csrf = old
	visitor := s.owner.visitor(t)
	requireStatus(t, visitor.send(t, "GET", "/api/v1/notifications", "", nil), 401, nil)
}

func TestNotifications_WhenGroupActivityOccurs_RestrictsRecipientsAndPrivateSuggestions(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	var invitation domain.GroupInvitationResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/invitations", fmt.Sprintf(`{"memberId":%q}`, s.memberID), nil), 201, &invitation)
	notificationOfKind(t, s.member, "groupInvitation")
	requireNotificationAbsent(t, s.foreign, "groupInvitation")
	requireStatus(t, s.member.send(t, "POST", "/api/v1/group-invitations/"+invitation.Id+"/accept", "", nil), 200, nil)
	requireNotificationAbsent(t, s.member, "groupInvitation")
	notificationOfKind(t, s.owner, "groupJoined")
	s.accept(t, s.foreign, s.foreignID)
	list := groupVisibilityList(t, s)
	own := "/api/v1/wishlists/" + list.Id
	shared := s.base + "/wishlists/" + list.Id
	requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
	notificationOfKind(t, s.member, "groupWishlistShared")
	requireNotificationAbsent(t, s.owner, "groupWishlistShared")
	var follow domain.WishlistSubscriptionResponse
	requireStatus(t, s.member.send(t, "POST", shared+"/subscriptions", "", nil), 201, &follow)
	if follow.ShareUrl != "http://localhost:5173/groups/"+s.group.Id+"/wishlists/"+list.Id {
		t.Fatal("private follow minted a public capability")
	}
	requireStatus(t, s.member.send(t, "GET", shared+"/subscriptions/current", "", nil), 200, nil)
	requireStatus(t, s.owner.send(t, "POST", own+"/wishes", `{"name":"Private wish"}`, nil), 201, nil)
	n := notificationOfKind(t, s.member, "wishesAdded")
	var destination struct {
		Href string `json:"href"`
	}
	requireStatus(t, s.member.send(t, "GET", "/api/v1/notifications/"+n.Id+"/destination", "", nil), 200, &destination)
	if destination.Href != follow.ShareUrl {
		t.Fatal("group destination changed scope")
	}
	requireStatus(t, s.member.send(t, "POST", shared+"/suggestions", `{"name":"Hidden idea"}`, nil), 201, nil)
	notificationOfKind(t, s.foreign, "suggestionAdded")
	requireNotificationAbsent(t, s.owner, "suggestionAdded")
	requireNotificationAbsent(t, s.member, "suggestionAdded")
	requireStatus(t, s.owner.send(t, "DELETE", shared, "", nil), 204, nil)
	requireNotificationAbsent(t, s.member, "wishesAdded")
	requireNotificationAbsent(t, s.member, "groupWishlistShared")
	requireNotificationAbsent(t, s.foreign, "suggestionAdded")
}

func TestNotifications_WhenReserving_UsesCurrentSurpriseModeAndNamesTheParticipantAndWish(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Visible reservations","occasion":"other","surpriseMode":false}`, nil), 201, &list)
	own := "/api/v1/wishlists/" + list.Id
	shared := s.base + "/wishlists/" + list.Id
	var wish domain.WishResponse
	created := s.owner.send(t, "POST", own+"/wishes", `{"name":"Headphones","quantity":3}`, nil)
	wishTag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
	reservationPath := shared + "/wishes/" + wish.Id + "/reservations/current"
	reserved := s.member.send(t, "PUT", reservationPath, `{"quantity":1}`, nil)
	reservationTag := reserved.Header.Get("ETag")
	requireStatus(t, reserved, 201, nil)
	// Act / Assert
	n := notificationOfKind(t, s.owner, "wishReserved")
	if n.ActorDisplayName == nil || *n.ActorDisplayName != "Test member" || n.WishName == nil || *n.WishName != "Headphones" {
		t.Fatal("missing authorized participant and wish")
	}
	setMode := func(surprise bool) {
		t.Helper()
		read := s.owner.send(t, "GET", own, "", nil)
		tag := read.Header.Get("ETag")
		requireStatus(t, read, 200, nil)
		requireStatus(t, s.owner.send(t, "PUT", own, fmt.Sprintf(`{"name":"Visible reservations","occasion":"other","surpriseMode":%t}`, surprise), map[string]string{"If-Match": tag}), 200, nil)
	}
	setMode(true)
	requireNotificationAbsent(t, s.owner, "wishReserved")
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/notifications/"+n.Id+"/read", "", nil), 404, nil)
	setMode(false)
	notificationOfKind(t, s.owner, "wishReserved")
	requireStatus(t, s.member.send(t, "DELETE", reservationPath, "", map[string]string{"If-Match": reservationTag}), 204, nil)
	setMode(true)
	requireStatus(t, s.member.send(t, "PUT", reservationPath, `{"quantity":1}`, nil), 201, nil)
	setMode(false)
	requireNotificationAbsent(t, s.owner, "wishReserved")
	// Deleting a wished item informs its reserving member using their own history.
	requireStatus(t, s.owner.send(t, "DELETE", own+"/wishes/"+wish.Id, "", map[string]string{"If-Match": wishTag}), 204, nil)
	notificationOfKind(t, s.member, "reservationUnavailable")
}

func TestNotifications_WhenReminderThresholdsAreReached_CreatesOneReminderAndHonorsDismissal(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	var follow domain.WishlistSubscriptionResponse
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, &follow)
	// Preserve session validity while controlling only the scheduler's application clock.
	previous := s.owner.service.Now
	t.Cleanup(func() { s.owner.service.Now = previous })
	date := time.Date(2027, 1, 11, 12, 0, 0, 0, time.UTC)
	s.owner.service.Now = func() time.Time { return date }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = previous
	n := notificationOfKind(t, s.member, "occasionReminder")
	if n.DaysRemaining == nil || *n.DaysRemaining != 7 {
		t.Fatal("incorrect UTC occasion threshold")
	}
	s.owner.service.Now = func() time.Time { return time.Date(2027, 1, 17, 12, 0, 0, 0, time.UTC) }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = previous
	n = notificationOfKind(t, s.member, "occasionReminder")
	if n.DaysRemaining == nil || *n.DaysRemaining != 1 || len(notificationPage(t, s.member)) != 2 {
		t.Fatal("one-day reminder is missing or duplicated")
	}
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Unshared reminder","occasion":"other"}`, nil), 201, &list)
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Ready"}`, nil), 201, nil)
	s.owner.service.Now = func() time.Time { return list.CreatedAt.Add(48*time.Hour - time.Second) }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = previous
	requireNotificationAbsent(t, s.owner, "sharingReminder")
	s.owner.service.Now = func() time.Time { return list.CreatedAt.Add(48 * time.Hour) }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = previous
	n = notificationOfKind(t, s.owner, "sharingReminder")
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/"+n.Id+"/dismiss", "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/notifications/"+n.Id+"/dismiss", "", nil), 204, nil)
	requireNotificationAbsent(t, s.owner, "sharingReminder")
	s.owner.service.Now = func() time.Time { return list.CreatedAt.Add(96 * time.Hour) }
	if err := s.owner.service.GenerateNotificationReminders(t.Context()); err != nil {
		t.Fatal(err)
	}
	s.owner.service.Now = previous
	requireNotificationAbsent(t, s.owner, "sharingReminder")
}

func TestNotifications_WhenActivityCommits_StreamsBadgeAndReadChangesWithoutPolling(t *testing.T) {
	// Arrange: the listener is shared by all streams and belongs to the server lifetime.
	s := newSubscriptionScenario(t)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	api := httpapi.New(s.owner.service, slog.New(slog.NewJSONHandler(io.Discard, nil)))
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	listenerDone := make(chan struct{})
	go func() { defer close(listenerDone); api.RunNotificationEvents(ctx) }()
	server := httptest.NewServer(api)
	defer func() { cancel(); <-listenerDone; server.Close() }()
	req, err := http.NewRequestWithContext(ctx, "GET", server.URL+"/api/v1/notifications/events", nil)
	if err != nil {
		t.Fatal(err)
	}
	req.Header.Set("Authorization", "Bearer "+s.member.access)
	stream, err := s.member.client.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	defer stream.Body.Close()
	if stream.StatusCode != 200 || stream.Header.Get("Content-Type") != "text/event-stream" {
		t.Fatal("missing authenticated event stream")
	}
	scanner := bufio.NewScanner(stream.Body)
	next := func(want bool) {
		t.Helper()
		for scanner.Scan() {
			if strings.HasPrefix(scanner.Text(), "data: ") {
				var value domain.NotificationStatusResponse
				if err = json.Unmarshal([]byte(strings.TrimPrefix(scanner.Text(), "data: ")), &value); err != nil {
					t.Fatal(err)
				}
				if value.HasUnread == want {
					return
				}
			}
		}
		t.Fatal("event stream ended before expected activity")
	}
	next(false)
	// Act / Assert: a rolled-back insert must not produce visible notification activity.
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Live wish"}`, nil), 201, nil)
	next(true)
	requireStatus(t, s.member.send(t, "POST", "/api/v1/notifications/read", "", nil), 204, nil)
	next(false)
}
