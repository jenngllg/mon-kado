//go:build integration

package httpapi_test

import (
	"bufio"
	"context"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/persistence"
	"github.com/jackc/pgx/v5/pgxpool"
)

func TestNotifications_WhenPersistenceRejectsActivity_RollsBackTheEntireBusinessAction(t *testing.T) {
	// Arrange: authorized recipients exist before the PostgreSQL fault is enabled.
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var first, second domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Atomic notifications","occasion":"other","surpriseMode":false}`, nil), 201, &first)
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Unshared atomic list","occasion":"other"}`, nil), 201, &second)
	own, shared := "/api/v1/wishlists/"+first.Id, s.base+"/wishlists/"+first.Id
	other := "/api/v1/wishlists/" + second.Id
	audiencePath := other + "/group-visibility"
	var wish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", own+"/wishes", `{"name":"Existing wish"}`, nil), 201, &wish)
	requireStatus(t, s.owner.send(t, "PUT", shared, "", nil), 201, nil)
	requireStatus(t, s.member.send(t, "POST", shared+"/subscriptions", "", nil), 201, nil)
	var link domain.GroupJoinLinkResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/join-link", "", nil), 201, &link)
	visibility := s.owner.send(t, "GET", audiencePath, "", nil)
	version := visibility.Header.Get("ETag")
	requireStatus(t, visibility, 200, nil)
	for _, member := range []*fixture{s.owner, s.member} {
		requireStatus(t, member.send(t, "POST", "/api/v1/notifications/read", "", nil), 204, nil)
	}
	beforeOwner, beforeMember := len(notificationPage(t, s.owner)), len(notificationPage(t, s.member))
	if _, err := s.owner.store.Pool.Exec(t.Context(), `ALTER TABLE member_notifications ADD CONSTRAINT notification_failure_test CHECK(false) NOT VALID`); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.WithoutCancel(t.Context()), 5*time.Second)
		defer cancel()
		if _, err := s.owner.store.Pool.Exec(ctx, `ALTER TABLE member_notifications DROP CONSTRAINT notification_failure_test`); err != nil {
			t.Error(err)
		}
	})

	// Act: every action must fail together with its notification, never commit partially.
	for _, scenario := range []struct {
		name, method, path, body string
		member                   *fixture
		headers                  map[string]string
	}{
		{"wish", "POST", own + "/wishes", `{"name":"Must roll back"}`, s.owner, nil},
		{"reservation", "PUT", shared + "/wishes/" + wish.Id + "/reservations/current", `{"quantity":1}`, s.member, nil},
		{"invitation", "POST", s.base + "/invitations", fmt.Sprintf(`{"memberId":%q}`, s.foreignID), s.owner, nil},
		{"join code", "POST", "/api/v1/groups/join", "", s.foreign, map[string]string{"X-MonKado-Share-Token": link.Code}},
		{"group grant", "PUT", s.base + "/wishlists/" + second.Id, "", s.owner, nil},
		{"public sharing", "POST", other + "/share-link", "", s.owner, nil},
		{"audience choices", "PUT", audiencePath, fmt.Sprintf(`{"groups":[{"groupId":%q,"isVisible":true}]}`, s.group.Id), s.owner, map[string]string{"If-Match": version}},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			requireStatus(t, scenario.member.send(t, scenario.method, scenario.path, scenario.body, scenario.headers), 500, nil)
		})
	}

	// Assert through public reads, including the unchanged audience version.
	var collection struct{ Wishes []domain.WishResponse }
	requireStatus(t, s.owner.send(t, "GET", own+"/wishes", "", nil), 200, &collection)
	if len(collection.Wishes) != 1 || collection.Wishes[0].Id != wish.Id {
		t.Fatal("a failed notification committed its new wish")
	}
	var reserved domain.SharedWishDetailResponse
	requireStatus(t, s.member.send(t, "GET", shared+"/wishes/"+wish.Id, "", nil), 200, &reserved)
	if reserved.ReservedQuantity == nil || *reserved.ReservedQuantity != 0 {
		t.Fatal("a failed notification committed its reservation")
	}
	after := s.owner.send(t, "GET", audiencePath, "", nil)
	if after.Header.Get("ETag") != version {
		t.Fatal("a failed sharing action changed the audience version")
	}
	requireStatus(t, after, 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "GET", other+"/share-link", "", nil), 404, nil)
	if len(notificationPage(t, s.owner)) != beforeOwner || len(notificationPage(t, s.member)) != beforeMember {
		t.Fatal("a failed action changed private notification history")
	}
}

func TestNotifications_WhenStreamCapacityIsReached_RefusesExtraReadersAndReleasesRevokedSessions(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	s.owner.service.Config.NotificationOptions.MaximumStreams = 1
	s.owner.service.Config.NotificationOptions.HeartbeatInterval = time.Millisecond
	api := httpapi.New(s.owner.service, slog.New(slog.NewJSONHandler(io.Discard, nil)))
	server := httptest.NewServer(api)
	defer server.Close()
	ctx, cancel := context.WithTimeout(t.Context(), 5*time.Second)
	defer cancel()
	open := func(token string) *http.Response {
		t.Helper()
		r, err := http.NewRequestWithContext(ctx, "GET", server.URL+"/api/v1/notifications/events", nil)
		if err != nil {
			t.Fatal(err)
		}
		r.Header.Set("Authorization", "Bearer "+token)
		response, err := s.member.client.Do(r)
		if err != nil {
			t.Fatal(err)
		}
		return response
	}
	first := open(s.member.access)
	defer first.Body.Close()
	if first.StatusCode != 200 {
		t.Fatal("initial stream refused")
	}
	reader := bufio.NewScanner(first.Body)
	for reader.Scan() && !strings.HasPrefix(reader.Text(), ": keepalive") {
		// Consume the initial event before checking the quiet-stream heartbeat.
	}
	if reader.Err() != nil || !strings.HasPrefix(reader.Text(), ": keepalive") {
		t.Fatal("quiet stream did not send its heartbeat")
	}

	// Act / Assert
	extra := open(s.owner.access)
	requireStatus(t, extra, 503, nil)
	requireStatus(t, s.member.send(t, "DELETE", "/api/v1/auth/sessions/current", "", nil), 204, nil)
	for reader.Scan() {
		// A revoked session must reach EOF rather than keep receiving heartbeats.
	}
	if reader.Err() != nil {
		t.Fatal(reader.Err())
	}
	replacement := open(s.owner.access)
	defer replacement.Body.Close()
	if replacement.StatusCode != 200 {
		t.Fatal("revoked stream retained its capacity slot")
	}
}

type notificationFailureLog struct {
	cancel context.CancelFunc
	calls  int
}

func (h *notificationFailureLog) Enabled(context.Context, slog.Level) bool { return true }
func (h *notificationFailureLog) Handle(_ context.Context, record slog.Record) error {
	if record.Message == "Notification listener interrupted" {
		h.calls++
		if h.calls == 2 {
			h.cancel()
		}
	}
	return nil
}
func (h *notificationFailureLog) WithAttrs([]slog.Attr) slog.Handler { return h }
func (h *notificationFailureLog) WithGroup(string) slog.Handler      { return h }

func TestNotifications_WhenListenerCannotAcquireItsConnection_RetriesAndHonorsCancellation(t *testing.T) {
	// Arrange: a closed dependency deterministically fails each acquisition.
	f := newFixture(t)
	pool, err := pgxpool.NewWithConfig(t.Context(), f.store.Pool.Config())
	if err != nil {
		t.Fatal(err)
	}
	pool.Close()
	cfg := f.service.Config
	cfg.NotificationOptions.ReconnectDelay = time.Nanosecond
	service := application.New(&persistence.Store{Pool: pool}, cfg, f.service.Now)
	ctx, cancel := context.WithCancel(t.Context())
	defer cancel()
	handler := &notificationFailureLog{cancel: cancel}
	api := httpapi.New(service, slog.New(handler))
	// Act
	api.RunNotificationEvents(ctx)
	// Assert
	if handler.calls != 2 || ctx.Err() == nil {
		t.Fatal("listener ignored its retry or cancellation boundary")
	}
}

func TestNotifications_WhenAnotherSchedulerOwnsTheLease_SkipsWithoutCreatingReminders(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
	tx, err := s.owner.store.Pool.Begin(t.Context())
	if err != nil {
		t.Fatal(err)
	}
	defer tx.Rollback(t.Context())
	if _, err = tx.Exec(t.Context(), `SELECT pg_advisory_xact_lock(771206490)`); err != nil {
		t.Fatal(err)
	}
	previous := s.owner.service.Now
	s.owner.service.Now = func() time.Time { return time.Date(2027, 1, 11, 12, 0, 0, 0, time.UTC) }
	defer func() { s.owner.service.Now = previous }()
	ctx, cancel := context.WithTimeout(t.Context(), 5*time.Second)
	defer cancel()
	// Act
	err = s.owner.service.GenerateNotificationReminders(ctx)
	s.owner.service.Now = previous
	// Assert
	if err != nil {
		t.Fatal(err)
	}
	requireNotificationAbsent(t, s.member, "occasionReminder")
}

func TestNotifications_WhenReminderStorageFails_RollsBackEveryReminderInTheBatch(t *testing.T) {
	for _, rejected := range []string{"occasionReminder", "sharingReminder"} {
		t.Run(rejected, func(t *testing.T) {
			// Arrange: the sharing failure follows a successful occasion insert in this transaction.
			s := newSubscriptionScenario(t)
			requireStatus(t, s.member.send(t, "POST", s.shared+"/subscriptions", "", s.headers), 201, nil)
			var list domain.WishlistResponse
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Unshared rollback","occasion":"other"}`, nil), 201, &list)
			requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Ready"}`, nil), 201, nil)
			constraint := `ALTER TABLE member_notifications ADD CONSTRAINT reminder_failure_test CHECK(kind <> '` + rejected + `') NOT VALID`
			if _, err := s.owner.store.Pool.Exec(t.Context(), constraint); err != nil {
				t.Fatal(err)
			}
			t.Cleanup(func() {
				ctx, cancel := context.WithTimeout(context.WithoutCancel(t.Context()), 5*time.Second)
				defer cancel()
				if _, err := s.owner.store.Pool.Exec(ctx, `ALTER TABLE member_notifications DROP CONSTRAINT reminder_failure_test`); err != nil {
					t.Error(err)
				}
			})
			previous := s.owner.service.Now
			s.owner.service.Now = func() time.Time { return time.Date(2027, 1, 11, 12, 0, 0, 0, time.UTC) }
			defer func() { s.owner.service.Now = previous }()
			// Act
			err := s.owner.service.GenerateNotificationReminders(t.Context())
			s.owner.service.Now = previous
			// Assert through both members' public notification histories.
			if err == nil {
				t.Fatal("scheduler ignored a failed reminder insert")
			}
			requireNotificationAbsent(t, s.member, "occasionReminder")
			requireNotificationAbsent(t, s.owner, "sharingReminder")
		})
	}
}
