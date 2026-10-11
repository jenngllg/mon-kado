//go:build integration

package httpapi_test

import (
	"archive/zip"
	"bytes"
	"context"
	"encoding/json"
	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/worker"
	"io"
	"log/slog"
	"strings"
	"testing"
	"time"
)

type emailFake struct{ Messages []application.EmailMessage }

func (f *emailFake) Send(ctx context.Context, id string, message application.EmailMessage) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	f.Messages = append(f.Messages, message)
	return "fake-message", nil
}

func TestExports_WhenWorkerPublishesArchive_PreservesOwnershipPrivacyAndExpiration(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "exporter@example.test")
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.service.Crypto.Now = f.service.Now
	f.service.Config.MaximumAttempts = 10
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Private birthday","occasion":"birthday"}`, nil), 201, &list)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"A secret gift","price":12.34}`, nil), 201, nil)
	var export domain.PersonalDataExportResponse
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/members/current/data-exports", "", nil), 202, &export)
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/data-exports/"+export.Id+"/archive", "", nil), 409, nil)
	sender := &emailFake{}
	w := worker.Worker{Service: f.service, Sender: sender, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/data-exports/latest", "", nil), 200, &export)
	download := f.send(t, "GET", "/api/v1/members/current/data-exports/"+export.Id+"/archive", "", nil)
	content, err := io.ReadAll(download.Body)
	download.Body.Close()
	if err != nil {
		t.Fatal(err)
	}
	archive, err := zip.NewReader(bytes.NewReader(content), int64(len(content)))
	if err != nil {
		t.Fatal(err)
	}
	var document map[string]any
	for _, entry := range archive.File {
		if entry.Name == "data.json" {
			file, err := entry.Open()
			if err != nil {
				t.Fatal(err)
			}
			if err = json.NewDecoder(file).Decode(&document); err != nil {
				t.Fatal(err)
			}
			file.Close()
		}
	}
	// Assert
	if download.StatusCode != 200 || download.Header.Get("Content-Type") != "application/zip" || export.Status != "ready" || export.SizeInBytes == nil || *export.SizeInBytes != int64(len(content)) || len(sender.Messages) != 1 || document["schemaVersion"] != float64(1) {
		t.Fatalf("publication invariant failed: %d %s %d", download.StatusCode, export.Status, len(sender.Messages))
	}
	data, _ := json.Marshal(document)
	for _, forbidden := range []string{"passwordHash", "refreshToken", "securityStamp", "protectedSecret", "accessToken"} {
		if strings.Contains(string(data), forbidden) {
			t.Fatalf("archive leaks %s", forbidden)
		}
	}
	now = now.Add(25 * time.Hour)
	f.access = ""
	f.csrfToken(t)
	var tokens domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", `{"email":"exporter@example.test","password":"A long test password 🎁"}`, nil), 200, &tokens)
	f.access = tokens.AccessToken
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/data-exports/"+export.Id+"/archive", "", nil), 404, nil)
}
