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

func exportDocument(t *testing.T, f *fixture) (domain.PersonalDataExportResponse, map[string]any, string) {
	t.Helper()
	var result domain.PersonalDataExportResponse
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
		t.Fatal("export archive unavailable")
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
		var document map[string]any
		if err := json.Unmarshal(data, &document); err != nil {
			t.Fatal(err)
		}
		return result, document, string(data)
	}
	t.Fatal("export data document missing")
	return result, nil, ""
}

func TestExports_WhenFlagsAndSubscriptionsExist_ExportsOwnStateAndMinimalSubscriptionMetadata(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "export-followed-owner@example.test")
	var identity domain.CurrentSessionResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &identity)
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Confidential followed list","occasion":"other","surpriseMode":true}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	var wish domain.WishResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Confidential favorite gift","note":"Confidential owner note","color":"Coral","size":"Medium","isFavorite":true}`, nil), 201, &wish)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	subscriber := f.visitor(t)
	subscriber.member(t, "export-subscriber@example.test")
	var subscription domain.WishlistSubscriptionResponse
	requireStatus(t, subscriber.send(t, "POST", "/api/v1/shared-wishlists/"+link.Id+"/subscriptions", "", map[string]string{"X-MonKado-Share-Token": u.Fragment}), 201, &subscription)
	requireStatus(t, f.send(t, "PATCH", "/api/v1/wishlists/"+list.Id, `{"isArchived":true}`, map[string]string{"If-Match": version}), 200, nil)
	// Act
	ownerExport, ownerDocument, ownerJSON := exportDocument(t, f)
	subscriberExport, subscriberDocument, subscriberJSON := exportDocument(t, subscriber)
	// Assert
	ownedLists := ownerDocument["wishlists"].([]any)
	ownedWishes := ownerDocument["wishes"].([]any)
	if len(ownedLists) != 1 || ownedLists[0].(map[string]any)["isArchived"] != true || ownedLists[0].(map[string]any)["surpriseMode"] != true ||
		len(ownedWishes) != 1 || ownedWishes[0].(map[string]any)["isFavorite"] != true {
		t.Fatal("export omitted archive, surprise or favorite state")
	}
	profile := ownerDocument["account"].(map[string]any)["profile"].(map[string]any)
	if profile["displayNameTag"] != identity.DisplayNameTag || ownedWishes[0].(map[string]any)["color"] != "Coral" || ownedWishes[0].(map[string]any)["size"] != "Medium" {
		t.Fatal("export omitted the immutable member tag or wish variants")
	}
	if len(ownerDocument["wishlistSubscriptions"].([]any)) != 0 || strings.Contains(ownerJSON, subscription.Id) {
		t.Fatal("owner export disclosed another member's subscription")
	}
	items := subscriberDocument["wishlistSubscriptions"].([]any)
	if len(items) != 1 {
		t.Fatal("subscriber export lost its own subscription history")
	}
	item := items[0].(map[string]any)
	if len(item) != 4 || item["id"] != subscription.Id || item["wishlistId"] != list.Id || item["createdAt"] == nil || item["updatedAt"] != nil {
		t.Fatal("subscription export differs from the minimal metadata contract")
	}
	for _, forbidden := range []string{u.Fragment, link.Id, "Confidential followed list", wish.Id, "Confidential favorite gift", "Confidential owner note", "shareUrl", "secretHash", "protectedSecret"} {
		if strings.Contains(subscriberJSON, forbidden) {
			t.Fatal("subscription export disclosed foreign content or access material")
		}
	}
	for _, resource := range []string{"wishlists", "wishes", "sharing", "participations", "reservations", "reservationHistory"} {
		if len(subscriberDocument[resource].([]any)) != 0 {
			t.Fatal("following a list implicitly exported its private resources")
		}
	}
	requireStatus(t, subscriber.send(t, "GET", "/api/v1/members/current/data-exports/"+ownerExport.Id+"/archive", "", nil), 404, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/members/current/data-exports/"+subscriberExport.Id+"/archive", "", nil), 404, nil)
}
