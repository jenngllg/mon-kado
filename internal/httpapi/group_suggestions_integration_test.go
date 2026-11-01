//go:build integration

package httpapi_test

import (
	"archive/zip"
	"bytes"
	"encoding/json"
	"io"
	"net/url"
	"path/filepath"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func suggestionScenario(t *testing.T) (groupScenario, string, string) {
	t.Helper()
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	s.accept(t, s.foreign, s.foreignID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Birthday","occasion":"other"}`, nil), 201, &list)
	groupList := s.base + "/wishlists/" + list.Id
	requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 201, nil)
	return s, list.Id, groupList + "/suggestions"
}

func TestGroupSuggestions_WhenAccessChanges_RevokesDetailImageAndHistory(t *testing.T) {
	for _, action := range []string{"leave", "remove", "archive", "suspend", "deleteGroup"} {
		t.Run(action, func(t *testing.T) {
			// Arrange
			s, listID, collection := suggestionScenario(t)
			var idea domain.GroupSuggestionResponse
			created := s.member.send(t, "POST", collection, `{"name":"Private portrait"}`, nil)
			tag, path := created.Header.Get("ETag"), created.Header.Get("Location")
			requireStatus(t, created, 201, &idea)
			requireStatus(t, s.member.upload(t, path+"/image", tag, 42, map[string]string{"X-CSRF-TOKEN": s.member.csrf}), 200, &idea)
			requireStatus(t, s.foreign.send(t, "PUT", path+"/reservations/current", `{"quantity":1}`, nil), 201, nil)
			// Act
			switch action {
			case "leave":
				requireStatus(t, s.foreign.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
			case "remove":
				requireStatus(t, s.owner.send(t, "DELETE", s.base+"/members/"+s.foreignID, "", nil), 204, nil)
			case "archive":
				own := "/api/v1/wishlists/" + listID
				read := s.owner.send(t, "GET", own, "", nil)
				version := read.Header.Get("ETag")
				requireStatus(t, read, 200, nil)
				requireStatus(t, s.owner.send(t, "PATCH", own, `{"isArchived":true}`, map[string]string{"If-Match": version}), 200, nil)
			case "suspend":
				if _, err := s.owner.store.Pool.Exec(t.Context(), `UPDATE wishlists SET is_suspended=true,suspension_reason='Test',suspended_at=now() WHERE id=$1`, listID); err != nil {
					t.Fatal(err)
				}
			case "deleteGroup":
				requireStatus(t, s.owner.send(t, "DELETE", s.base, "", map[string]string{"If-Match": s.etag}), 204, nil)
			}
			// Assert: previously known paths and history no longer disclose content.
			for _, target := range []string{collection, path, path + "/image", path + "/reservations/current"} {
				requireStatus(t, s.foreign.send(t, "GET", target, "", nil), 404, nil)
			}
			var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
			requireStatus(t, s.foreign.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			if history.TotalCount != 0 {
				t.Fatal("revoked idea remained in reservation history")
			}
		})
	}
}

func TestGroupSuggestions_WhenMemberRejoins_RestoresOnlyOriginalGroupAccess(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	var idea domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", collection, `{"name":"Private surprise"}`, nil)
	path := created.Header.Get("Location")
	requireStatus(t, created, 201, &idea)
	reservation := s.foreign.send(t, "PUT", path+"/reservations/current", `{"quantity":1}`, nil)
	tag := reservation.Header.Get("ETag")
	requireStatus(t, reservation, 201, nil)
	// Act / Assert
	requireStatus(t, s.foreign.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 404, nil)
	s.accept(t, s.foreign, s.foreignID)
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 200, &idea)
	if idea.ReservedQuantity != 1 || idea.CurrentParticipantReservedQuantity != 1 {
		t.Fatal("rejoining lost the existing reservation")
	}
	requireStatus(t, s.foreign.send(t, "DELETE", path+"/reservations/current", "", map[string]string{"If-Match": tag}), 204, nil)
}

func TestGroupSuggestions_WhenMemberJoinsAndListIsRestored_RevealsOnlyActiveGroupIdeas(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	s.accept(t, s.member, s.memberID)
	var list domain.WishlistResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists", `{"name":"Birthday","occasion":"other"}`, nil), 201, &list)
	owned, groupList := "/api/v1/wishlists/"+list.Id, s.base+"/wishlists/"+list.Id
	requireStatus(t, s.owner.send(t, "PUT", groupList, "", nil), 201, nil)
	var idea domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", groupList+"/suggestions", `{"name":"Surprise after invitation"}`, nil)
	path := created.Header.Get("Location")
	requireStatus(t, created, 201, &idea)
	// Act / Assert: pending or foreign identities never get access; admission does.
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 404, nil)
	s.accept(t, s.foreign, s.foreignID)
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 200, &idea)
	read := s.owner.send(t, "GET", owned, "", nil)
	tag := read.Header.Get("ETag")
	requireStatus(t, read, 200, nil)
	archived := s.owner.send(t, "PATCH", owned, `{"isArchived":true}`, map[string]string{"If-Match": tag})
	archivedTag := archived.Header.Get("ETag")
	requireStatus(t, archived, 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 404, nil)
	requireStatus(t, s.owner.send(t, "PATCH", owned, `{"isArchived":false}`, map[string]string{"If-Match": archivedTag}), 200, nil)
	requireStatus(t, s.foreign.send(t, "GET", path, "", nil), 200, &idea)
	if idea.Name != "Surprise after invitation" {
		t.Fatal("restoration did not preserve the original suggestion")
	}
}

func suggestionExportData(t *testing.T, f *fixture, member string) string {
	t.Helper()
	id := security.NewID()
	if _, _, err := f.service.GenerateExport(t.Context(), member, id); err != nil {
		t.Fatal(err)
	}
	archive, err := zip.OpenReader(filepath.Join(f.service.Config.ExportPath, id+".zip"))
	if err != nil {
		t.Fatal(err)
	}
	defer archive.Close()
	for _, entry := range archive.File {
		if entry.Name != "data.json" {
			continue
		}
		reader, err := entry.Open()
		if err != nil {
			t.Fatal(err)
		}
		data, err := io.ReadAll(reader)
		reader.Close()
		if err != nil {
			t.Fatal(err)
		}
		return string(data)
	}
	t.Fatal("export data is missing")
	return ""
}

func TestGroupSuggestions_WhenExporting_ExcludesBeneficiaryAndOtherMembersReservations(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	var idea domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", collection, `{"name":"Unique secret vase"}`, nil)
	path, tag := created.Header.Get("Location"), created.Header.Get("ETag")
	requireStatus(t, created, 201, &idea)
	requireStatus(t, s.member.upload(t, path+"/image", tag, 42, map[string]string{"X-CSRF-TOKEN": s.member.csrf}), 200, &idea)
	var reservation domain.GiftReservationResponse
	requireStatus(t, s.foreign.send(t, "PUT", path+"/reservations/current", `{"quantity":1}`, nil), 201, &reservation)
	// Act / Assert
	owner := suggestionExportData(t, s.owner, s.ownerID)
	if strings.Contains(owner, idea.Id) || strings.Contains(owner, "Unique secret vase") || strings.Contains(owner, reservation.Id) {
		t.Fatal("beneficiary export disclosed a private suggestion")
	}
	author := suggestionExportData(t, s.member, s.memberID)
	var authorData struct {
		GroupSuggestions []struct {
			ImageContentBase64 *string `json:"imageContentBase64"`
		} `json:"groupSuggestions"`
	}
	if err := json.Unmarshal([]byte(author), &authorData); err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(author, "Unique secret vase") || strings.Contains(author, reservation.Id) || len(authorData.GroupSuggestions) != 1 || authorData.GroupSuggestions[0].ImageContentBase64 == nil || !strings.HasPrefix(*authorData.GroupSuggestions[0].ImageContentBase64, "UklGR") {
		t.Fatal("author export omitted their idea or exposed another member's reservation")
	}
	reserver := suggestionExportData(t, s.foreign, s.foreignID)
	if !strings.Contains(reserver, reservation.Id) || !strings.Contains(reserver, "Unique secret vase") {
		t.Fatal("reserver export omitted their authorized reservation")
	}
	requireStatus(t, s.foreign.send(t, "DELETE", s.base+"/members/current", "", nil), 204, nil)
	revoked := suggestionExportData(t, s.foreign, s.foreignID)
	if strings.Contains(revoked, idea.Id) || strings.Contains(revoked, "Unique secret vase") || strings.Contains(revoked, reservation.Id) {
		t.Fatal("revoked group access leaked through export")
	}
}

func TestGroupSuggestions_WhenReservationsRace_EnforcesCapacity(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	var idea domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", collection, `{"name":"One book","quantity":1}`, nil)
	path := created.Header.Get("Location")
	requireStatus(t, created, 201, &idea)
	start, results := make(chan struct{}), make(chan int, 2)
	for _, member := range []*fixture{s.member, s.foreign} {
		go func(member *fixture) {
			<-start
			response := member.send(t, "PUT", path+"/reservations/current", `{"quantity":1}`, nil)
			response.Body.Close()
			results <- response.StatusCode
		}(member)
	}
	// Act
	close(start)
	first, second := <-results, <-results
	// Assert
	if !((first == 201 && second == 409) || (first == 409 && second == 201)) {
		t.Fatalf("capacity race returned %d and %d", first, second)
	}
}

func TestGroupSuggestions_WhenInputOrVersionsAreInvalid_ReturnsTargetedErrors(t *testing.T) {
	// Arrange
	s, _, collection := suggestionScenario(t)
	var validation domain.ErrorResponse
	// Act / Assert: independent input errors are aggregated by the request pipeline.
	requireStatus(t, s.member.send(t, "POST", collection, `{"name":"","url":"ftp://example.com","price":-1,"quantity":101}`, nil), 400, &validation)
	paths := map[string]bool{}
	for _, item := range validation.ValidationErrors {
		paths[item.PropertyName] = true
	}
	for _, path := range []string{"name", "url", "price", "quantity"} {
		if !paths[path] {
			t.Fatalf("missing validation path %s", path)
		}
	}
	var idea domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", collection, `{"name":"Painting","quantity":3}`, nil)
	tag, path := created.Header.Get("ETag"), created.Header.Get("Location")
	requireStatus(t, created, 201, &idea)
	requireStatus(t, s.member.send(t, "PUT", path, `{"name":"Painting"}`, nil), 428, nil)
	requireStatus(t, s.member.send(t, "DELETE", path, "", map[string]string{"If-Match": "bad"}), 400, nil)
	var reservation domain.GiftReservationResponse
	reserved := s.foreign.send(t, "PUT", path+"/reservations/current", `{"quantity":1}`, nil)
	reservationTag := reserved.Header.Get("ETag")
	requireStatus(t, reserved, 201, &reservation)
	requireStatus(t, s.foreign.send(t, "PUT", path+"/reservations/current", `{"quantity":2}`, nil), 428, nil)
	requireStatus(t, s.foreign.send(t, "PUT", path+"/reservations/current", `{"quantity":2}`, map[string]string{"If-Match": `"00000000"`}), 412, nil)
	changed := s.foreign.send(t, "PUT", path+"/reservations/current", `{"quantity":2}`, map[string]string{"If-Match": reservationTag})
	next := changed.Header.Get("ETag")
	requireStatus(t, changed, 200, &reservation)
	if reservation.Quantity != 2 || next == reservationTag {
		t.Fatal("reservation update was not versioned")
	}
	requireStatus(t, s.foreign.send(t, "DELETE", path+"/reservations/current", "", map[string]string{"If-Match": reservationTag}), 412, nil)
	requireStatus(t, s.foreign.send(t, "DELETE", path+"/reservations/current", "", map[string]string{"If-Match": next}), 204, nil)
	image := s.member.upload(t, path+"/image", tag, 42, map[string]string{"X-CSRF-TOKEN": s.member.csrf})
	imageTag := image.Header.Get("ETag")
	requireStatus(t, image, 200, &idea)
	requireStatus(t, s.member.send(t, "DELETE", path+"/image", "", map[string]string{"If-Match": tag}), 412, nil)
	removed := s.member.send(t, "DELETE", path+"/image", "", map[string]string{"If-Match": imageTag})
	removedTag := removed.Header.Get("ETag")
	requireStatus(t, removed, 204, nil)
	requireStatus(t, s.member.send(t, "GET", path+"/image", "", nil), 404, nil)
	requireStatus(t, s.member.send(t, "DELETE", path+"/image", "", map[string]string{"If-Match": removedTag}), 404, nil)
}

func TestGroupSuggestions_WhenReadingDifferentAudiences_ConfinesContentAndImages(t *testing.T) {
	// Arrange
	s, listID, collection := suggestionScenario(t)
	var suggestion domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", collection, `{"name":"Secret telescope","note":"Special note","url":"https://example.com/gift","price":42.50,"quantity":2}`, nil)
	etag, location := created.Header.Get("ETag"), created.Header.Get("Location")
	requireStatus(t, created, 201, &suggestion)
	if location != collection+"/"+suggestion.Id || etag == "" || suggestion.EntityTag != etag || !suggestion.CanEdit || suggestion.AuthorId == nil || *suggestion.AuthorId != s.memberID || suggestion.AuthorDisplayName == nil {
		t.Fatal("suggestion identity, author or version missing")
	}
	upload := s.member.upload(t, location+"/image", etag, 42, map[string]string{"X-CSRF-TOKEN": s.member.csrf})
	etag = upload.Header.Get("ETag")
	requireStatus(t, upload, 200, &suggestion)
	if !suggestion.HasImage || etag == "" {
		t.Fatal("suggestion image was not saved")
	}
	// Act / Assert: accepted non-beneficiary members see the idea and image.
	var seen []domain.GroupSuggestionResponse
	requireStatus(t, s.foreign.send(t, "GET", collection, "", nil), 200, &seen)
	if len(seen) != 1 || seen[0].Name != "Secret telescope" || seen[0].CanEdit || !seen[0].HasImage {
		t.Fatal("eligible group member did not see the labeled suggestion")
	}
	var image struct {
		Content []byte `json:"content"`
	}
	requireStatus(t, s.foreign.send(t, "GET", location+"/image", "", nil), 200, &image)
	if len(image.Content) == 0 || !bytes.HasPrefix(image.Content, []byte("RIFF")) {
		t.Fatal("authorized image is not normalized WebP")
	}
	for _, path := range []string{collection, location, location + "/image", location + "/reservations/current"} {
		requireStatus(t, s.owner.send(t, "GET", path, "", nil), 404, nil)
	}
	requireStatus(t, s.owner.send(t, "POST", collection, `{"name":"Forbidden"}`, nil), 404, nil)
	requireStatus(t, s.owner.send(t, "PUT", location, `{"name":"Forbidden"}`, map[string]string{"If-Match": etag}), 404, nil)
	requireStatus(t, s.foreign.visitor(t).send(t, "GET", location+"/image", "", nil), 401, nil)
	// A separate group and the owner's own/public list never acquire this idea.
	var other domain.GroupResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/groups", `{"name":"Friends"}`, nil), 201, &other)
	otherScenario := s
	otherScenario.base = "/api/v1/groups/" + other.Id
	otherScenario.accept(t, s.member, s.memberID)
	requireStatus(t, s.owner.send(t, "PUT", otherScenario.base+"/wishlists/"+listID, "", nil), 201, nil)
	otherCollection := otherScenario.base + "/wishlists/" + listID + "/suggestions"
	requireStatus(t, s.member.send(t, "GET", otherCollection, "", nil), 200, &seen)
	if len(seen) != 0 {
		t.Fatal("suggestion crossed its group scope")
	}
	requireStatus(t, s.member.send(t, "GET", otherCollection+"/"+suggestion.Id, "", nil), 404, nil)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, s.owner.send(t, "POST", "/api/v1/wishlists/"+listID+"/share-link", "", nil), 201, &link)
	parsed, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	for _, path := range []string{"/api/v1/wishlists/" + listID + "/wishes", "/api/v1/shared-wishlists/" + link.Id, s.base + "/wishlists/" + listID} {
		resp := s.owner.send(t, "GET", path, "", map[string]string{"X-MonKado-Share-Token": parsed.Fragment})
		body, err := io.ReadAll(resp.Body)
		resp.Body.Close()
		if err != nil || resp.StatusCode != 200 || strings.Contains(string(body), "Secret telescope") || strings.Contains(string(body), "Special note") || strings.Contains(string(body), suggestion.Id) {
			t.Fatal("beneficiary or public list disclosed a suggestion")
		}
	}
	guest := s.owner.visitor(t)
	public := guest.send(t, "GET", "/api/v1/shared-wishlists/"+link.Id, "", map[string]string{"X-MonKado-Share-Token": parsed.Fragment})
	content, err := io.ReadAll(public.Body)
	public.Body.Close()
	if err != nil || public.StatusCode != 200 || bytes.Contains(content, []byte("Secret telescope")) || bytes.Contains(content, []byte(suggestion.Id)) {
		t.Fatal("public visitor received a private group suggestion")
	}
}

func TestGroupSuggestions_WhenReserved_PreventsConflictingChangesAndProtectsHistory(t *testing.T) {
	// Arrange
	s, listID, collection := suggestionScenario(t)
	var suggestion domain.GroupSuggestionResponse
	created := s.member.send(t, "POST", collection, `{"name":"Hidden painting","quantity":2}`, nil)
	etag, location := created.Header.Get("ETag"), created.Header.Get("Location")
	requireStatus(t, created, 201, &suggestion)
	// Act / Assert: only the author can edit, stale writes do not overwrite.
	requireStatus(t, s.foreign.send(t, "PUT", location, `{"name":"Other edit"}`, map[string]string{"If-Match": etag}), 403, nil)
	updated := s.member.send(t, "PUT", location, `{"name":"Hidden painting","quantity":2,"note":"Blue"}`, map[string]string{"If-Match": etag})
	next := updated.Header.Get("ETag")
	requireStatus(t, updated, 200, &suggestion)
	requireStatus(t, s.member.send(t, "DELETE", location, "", map[string]string{"If-Match": etag}), 412, nil)
	reservationPath := location + "/reservations/current"
	var reservation domain.GiftReservationResponse
	reserved := s.foreign.send(t, "PUT", reservationPath, `{"quantity":2}`, nil)
	reservationETag := reserved.Header.Get("ETag")
	requireStatus(t, reserved, 201, &reservation)
	requireStatus(t, s.member.send(t, "PUT", reservationPath, `{"quantity":1}`, nil), 409, nil)
	requireStatus(t, s.member.send(t, "DELETE", location, "", map[string]string{"If-Match": next}), 409, nil)
	requireStatus(t, s.member.send(t, "PUT", location, `{"name":"Changed","quantity":2}`, map[string]string{"If-Match": next}), 409, nil)
	requireStatus(t, s.member.upload(t, location+"/image", next, 42, map[string]string{"X-CSRF-TOKEN": s.member.csrf}), 409, nil)
	var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
	requireStatus(t, s.foreign.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if history.TotalCount != 1 || history.Items[0].GroupSuggestionPath == nil || !strings.Contains(*history.Items[0].GroupSuggestionPath, suggestion.Id) {
		t.Fatal("reservation history omitted the suggestion's scoped navigation")
	}
	// Withdrawing the group share removes both direct and history access.
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/wishlists/"+listID, "", nil), 204, nil)
	requireStatus(t, s.foreign.send(t, "GET", location, "", nil), 404, nil)
	requireStatus(t, s.foreign.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
	if history.TotalCount != 0 {
		t.Fatal("withdrawn suggestion leaked through reservation history")
	}
	requireStatus(t, s.owner.send(t, "PUT", s.base+"/wishlists/"+listID, "", nil), 201, nil)
	requireStatus(t, s.foreign.send(t, "DELETE", reservationPath, "", map[string]string{"If-Match": reservationETag}), 204, nil)
	requireStatus(t, s.member.send(t, "DELETE", location, "", map[string]string{"If-Match": next}), 204, nil)
	requireStatus(t, s.foreign.send(t, "GET", location, "", nil), 404, nil)
}
