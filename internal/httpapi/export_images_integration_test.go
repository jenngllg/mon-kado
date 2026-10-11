//go:build integration

package httpapi_test

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"io"
	"log/slog"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestExports_WhenImagesAndPrivateReservationsExist_IncludesOwnedImagesWithoutParticipantSecrets(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "illustratedexport@example.test")
	current := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	updated := f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Export member","isVisibleInMemberSearch":false}`, map[string]string{"If-Match": etag})
	etag = updated.Header.Get("ETag")
	requireStatus(t, updated, 200, nil)
	requireStatus(t, f.upload(t, "/api/v1/members/current/profile/image", etag, 42), 200, nil)
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Illustrated list","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Illustrated gift"}`, nil)
	etag = created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	requireStatus(t, f.upload(t, "/api/v1/wishlists/"+list.Id+"/wishes/"+wish.Id+"/image", etag, 99), 200, nil)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	share, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	visitor := f.visitor(t)
	visitor.member(t, "privateparticipant@example.test")
	headers := map[string]string{"X-MonKado-Share-Token": share.Fragment}
	base := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, visitor.send(t, "POST", base+"/participants", "", headers), 201, nil)
	requireStatus(t, visitor.send(t, "PUT", base+"/wishes/"+wish.Id+"/reservations/current", `{"quantity":1}`, headers), 201, nil)
	var result domain.PersonalDataExportResponse
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 202, &result)
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	download := f.send(t, "GET", "/api/v1/members/current/data-exports/"+result.Id+"/archive", "", nil)
	content, err := io.ReadAll(download.Body)
	download.Body.Close()
	if err != nil || download.StatusCode != 200 {
		t.Fatal("image archive download failed")
	}
	archive, err := zip.NewReader(bytes.NewReader(content), int64(len(content)))
	if err != nil {
		t.Fatal(err)
	}
	// Assert
	entries := map[string][]byte{}
	for _, entry := range archive.File {
		file, err := entry.Open()
		if err != nil {
			t.Fatal(err)
		}
		entries[entry.Name], err = io.ReadAll(file)
		file.Close()
		if err != nil {
			t.Fatal(err)
		}
	}
	if len(entries) != 4 || len(entries["README.txt"]) == 0 {
		t.Fatalf("expected data, README and two owned images, got %d", len(entries))
	}
	for _, path := range []string{"images/profile.webp", "images/wishes/" + wish.Id + ".webp"} {
		picture := entries[path]
		if len(picture) < 12 || string(picture[:4]) != "RIFF" || string(picture[8:12]) != "WEBP" {
			t.Fatalf("missing normalized image at %s", path)
		}
	}
	var document map[string]any
	if err := json.Unmarshal(entries["data.json"], &document); err != nil {
		t.Fatal(err)
	}
	account, ok := document["account"].(map[string]any)
	if !ok {
		t.Fatal("account export missing")
	}
	profile, ok := account["profile"].(map[string]any)
	if !ok || profile["isVisibleInMemberSearch"] != false {
		t.Fatal("export omitted the saved member search preference")
	}
	for _, key := range []string{"participations", "reservations", "reservationHistory"} {
		items, valid := document[key].([]any)
		if !valid || len(items) != 0 {
			t.Fatalf("owner archive disclosed another participant's %s", key)
		}
	}
	for _, secret := range []string{share.Fragment, "privateparticipant@example.test", "secretHash", "protectedSecret", "passwordHash", "refreshToken"} {
		if strings.Contains(string(entries["data.json"]), secret) {
			t.Fatal("archive disclosed confidential participant or access material")
		}
	}
}
