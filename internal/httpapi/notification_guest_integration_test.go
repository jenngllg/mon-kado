//go:build integration

package httpapi_test

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"io"
	"log/slog"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestNotifications_WhenGuestReservesOutsideSurprise_ExposesOnlyAuthorizedNameAndFiltersTheExport(t *testing.T) {
	// Arrange
	s := newSubscriptionScenario(t)
	current := s.owner.send(t, "GET", s.base, "", nil)
	tag := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":"Followed birthday","occasion":"birthday","eventDate":"2027-01-18","surpriseMode":false}`, map[string]string{"If-Match": tag}), 200, nil)
	var wish domain.WishResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/wishes", `{"name":"Guest reserved wish"}`, nil), 201, &wish)
	guest := s.owner.visitor(t)
	requireStatus(t, guest.send(t, "POST", s.shared+"/participants", `{"displayName":"Confirmed guest"}`, s.headers), 201, nil)
	// Act
	requireStatus(t, guest.send(t, "PUT", s.shared+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, s.headers), 201, nil)
	// Assert
	n := notificationOfKind(t, s.owner, "wishReserved")
	if n.ActorDisplayName == nil || *n.ActorDisplayName != "Confirmed guest" || n.WishName == nil || *n.WishName != wish.Name {
		t.Fatal("guest notification lacks its authorized display name and wish")
	}
	current = s.owner.send(t, "GET", s.base, "", nil)
	tag = current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":"Followed birthday","occasion":"birthday","eventDate":"2027-01-18","surpriseMode":true}`, map[string]string{"If-Match": tag}), 200, nil)
	var export domain.PersonalDataExportResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 202, &export)
	w := worker.Worker{Service: s.owner.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	download := s.owner.send(t, "GET", "/api/v1/members/current/data-exports/"+export.Id+"/archive", "", nil)
	content, err := io.ReadAll(download.Body)
	download.Body.Close()
	if err != nil || download.StatusCode != 200 {
		t.Fatal("notification export is unavailable")
	}
	archive, err := zip.NewReader(bytes.NewReader(content), int64(len(content)))
	if err != nil {
		t.Fatal(err)
	}
	for _, entry := range archive.File {
		if entry.Name != "data.json" {
			continue
		}
		file, err := entry.Open()
		if err != nil {
			t.Fatal(err)
		}
		data, err := io.ReadAll(file)
		file.Close()
		if err != nil {
			t.Fatal(err)
		}
		var document struct {
			AccountActivity struct {
				Notifications []domain.NotificationResponse `json:"inAppNotifications"`
			} `json:"accountActivity"`
		}
		if err = json.Unmarshal(data, &document); err != nil {
			t.Fatal(err)
		}
		if document.AccountActivity.Notifications == nil {
			t.Fatal("in-app notification metadata is missing from the export")
		}
		for _, notification := range document.AccountActivity.Notifications {
			if notification.Kind == "wishReserved" {
				t.Fatal("surprise reservation leaked through export")
			}
		}
		if bytes.Contains(data, []byte("Confirmed guest")) {
			t.Fatal("guest identity leaked through export")
		}
		return
	}
	t.Fatal("export manifest was not found")
}
