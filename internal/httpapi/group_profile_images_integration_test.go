//go:build integration

package httpapi_test

import (
	"archive/zip"
	"bytes"
	"image"
	"io"
	"log/slog"
	"net/url"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/JennGllg/mon-kado/internal/worker"
)

func TestGroupProfileImages_WhenReplacedAndDeleted_UsesGroupVersionAndRemovesObsoleteFiles(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	path := s.base + "/image"
	var group domain.GroupResponse
	// Act
	upload := s.owner.upload(t, path, s.etag, 42, map[string]string{"X-CSRF-TOKEN": s.owner.csrf})
	etag := upload.Header.Get("ETag")
	requireStatus(t, upload, 200, &group)
	if group.ProfileImageUrl == nil || etag == s.etag || group.UpdatedAt == nil {
		t.Fatal("photo and updated group version were not returned")
	}
	old, _ := url.Parse(*group.ProfileImageUrl)
	read := s.owner.visitor(t).send(t, "GET", old.RequestURI(), "", nil)
	content, err := io.ReadAll(read.Body)
	read.Body.Close()
	if err != nil || read.StatusCode != 200 || read.Header.Get("Cache-Control") != "no-store" {
		t.Fatal("private photo read failed or was cacheable")
	}
	size, _, err := image.DecodeConfig(bytes.NewReader(content))
	if err != nil || size.Width != 4 || size.Height != 3 {
		t.Fatal("photo was not normalized to the member avatar format")
	}
	// Assert: outsiders and accepted non-owners cannot mutate, stale versions do not overwrite.
	requireStatus(t, s.foreign.upload(t, path, etag, 43, map[string]string{"X-CSRF-TOKEN": s.foreign.csrf}), 404, nil)
	requireStatus(t, s.member.upload(t, path, etag, 43, map[string]string{"X-CSRF-TOKEN": s.member.csrf}), 403, nil)
	requireStatus(t, s.owner.upload(t, path, s.etag, 43, map[string]string{"X-CSRF-TOKEN": s.owner.csrf}), 412, nil)
	for _, entry := range []struct {
		tag    string
		status int
	}{{"", 428}, {"bad", 400}} {
		requireStatus(t, s.owner.upload(t, path, entry.tag, 43, map[string]string{"X-CSRF-TOKEN": s.owner.csrf}), entry.status, nil)
	}
	requireStatus(t, s.owner.upload(t, path, etag, 43), 400, nil)
	var page domain.PaginatedResponse[domain.GroupResponse]
	requireStatus(t, s.member.send(t, "GET", "/api/v1/groups", "", nil), 200, &page)
	if len(page.Items) != 1 || page.Items[0].ProfileImageUrl == nil {
		t.Fatal("accepted member did not receive a group photo")
	}
	replaced := s.owner.upload(t, path, etag, 99, map[string]string{"X-CSRF-TOKEN": s.owner.csrf})
	etag = replaced.Header.Get("ETag")
	requireStatus(t, replaced, 200, &group)
	requireStatus(t, s.owner.send(t, "GET", old.RequestURI(), "", nil), 404, nil)
	current, _ := url.Parse(*group.ProfileImageUrl)
	deleted := s.owner.send(t, "DELETE", path, "", map[string]string{"If-Match": etag})
	etag = deleted.Header.Get("ETag")
	requireStatus(t, deleted, 204, nil)
	requireStatus(t, s.owner.send(t, "GET", current.RequestURI(), "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "DELETE", path, "", map[string]string{"If-Match": etag}), 404, nil)
	requireStatus(t, s.owner.send(t, "GET", s.base, "", nil), 200, &group)
	if group.ProfileImageUrl != nil {
		t.Fatal("removed photo remains referenced")
	}
	w := worker.Worker{Service: s.owner.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	if err = w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	entries, err := os.ReadDir(s.owner.service.Config.ImagePath)
	if err != nil || len(entries) != 0 {
		t.Fatal("obsolete group files were retained")
	}
}

func TestGroupProfileImages_WhenMembershipOrImageGrantExpires_RevokesPrivateAccess(t *testing.T) {
	for _, action := range []string{"leave", "remove", "deleteGroup", "expire"} {
		t.Run(action, func(t *testing.T) {
			// Arrange
			s := newGroupScenario(t)
			s.accept(t, s.member, s.memberID)
			var group domain.GroupResponse
			upload := s.owner.upload(t, s.base+"/image", s.etag, 42, map[string]string{"X-CSRF-TOKEN": s.owner.csrf})
			tag := upload.Header.Get("ETag")
			requireStatus(t, upload, 200, &group)
			requireStatus(t, s.member.send(t, "GET", s.base, "", nil), 200, &group)
			if group.ProfileImageUrl == nil {
				t.Fatal("missing member photo grant")
			}
			grant, _ := url.Parse(*group.ProfileImageUrl)
			requireStatus(t, s.member.send(t, "GET", grant.RequestURI(), "", nil), 200, nil)
			requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
			requireStatus(t, s.member.send(t, "GET", s.base+"/image?token=bad", "", nil), 404, nil)
			requireStatus(t, s.member.send(t, "GET", "/api/v1/groups/"+s.foreignID+"/image?"+grant.RawQuery, "", nil), 404, nil)
			// Act
			switch action {
			case "leave":
				requireStatus(t, s.member.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
			case "remove":
				requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.memberID, "", nil), 204, nil)
			case "deleteGroup":
				requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": tag}), 204, nil)
			case "expire":
				now := s.owner.service.Now()
				s.owner.service.Now = func() time.Time { return now.Add(6 * time.Minute) }
			}
			// Assert
			requireStatus(t, s.member.send(t, "GET", grant.RequestURI(), "", nil), 404, nil)
			if action == "deleteGroup" {
				w := worker.Worker{Service: s.owner.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
				t.Cleanup(w.Close)
				if err := w.Tick(t.Context()); err != nil {
					t.Fatal(err)
				}
				entries, err := os.ReadDir(s.owner.service.Config.ImagePath)
				if err != nil || len(entries) != 0 {
					t.Fatal("deleted group's photo was not cleaned")
				}
			}
		})
	}
}

func TestGroupProfileImages_WhenExporting_IncludesOnlyOrganizerOwnedPhotoBytes(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	requireStatus(t, s.owner.upload(t, s.base+"/image", s.etag, 42, map[string]string{"X-CSRF-TOKEN": s.owner.csrf}), 200, nil)
	for _, scenario := range []struct {
		member string
		photo  bool
	}{{s.ownerID, true}, {s.memberID, false}} {
		// Act
		id := security.NewID()
		if _, _, err := s.owner.service.GenerateExport(t.Context(), scenario.member, id); err != nil {
			t.Fatal(err)
		}
		archive, err := zip.OpenReader(filepath.Join(s.owner.service.Config.ExportPath, id+".zip"))
		if err != nil {
			t.Fatal(err)
		}
		found := false
		for _, file := range archive.File {
			if file.Name == "images/groups/"+s.group.Id+".webp" {
				found = true
			}
		}
		archive.Close()
		// Assert
		if found != scenario.photo {
			t.Fatal("export did not preserve group photo ownership")
		}
	}
}
