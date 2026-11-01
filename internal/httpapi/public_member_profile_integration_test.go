//go:build integration

package httpapi_test

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http/httptest"
	"net/url"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestPublicMemberProfile_WhenAnonymous_ReturnsOnlyConfirmedIdentityAndDiscoverableLists(t *testing.T) {
	// Arrange
	f := newFixture(t)
	createdAt := time.Date(2026, 10, 12, 12, 0, 0, 0, time.UTC)
	f.service.Now = func() time.Time { return createdAt }
	owner := f.member(t, "public-owner@example.test")
	ownerAccess := f.access
	f.member(t, "other-owner@example.test")
	var foreign domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Foreign","occasion":"other"}`, nil), 201, &foreign)
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+foreign.Id+"/share-link", "", nil), 201, nil)
	f.access = ownerAccess
	var visible []domain.WishlistResponse
	for _, name := range []string{"First", "Second", "Private", "Suspended", "Archived", "Newest"} {
		if name == "Newest" {
			createdAt = createdAt.Add(time.Second)
		}
		var list domain.WishlistResponse
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", jsonInput(t, map[string]any{"name": name, "occasion": "birthday", "eventDate": "2035-01-18"}), nil), 201, &list)
		if name != "Private" {
			requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, nil)
		}
		switch name {
		case "Suspended":
			// Seed moderation state without involving an unrelated administrative workflow.
			if _, err := f.store.Pool.Exec(t.Context(), `UPDATE wishlists SET is_suspended=true,suspension_reason='Private moderation reason',suspended_at=$2 WHERE id=$1`, list.Id, createdAt); err != nil {
				t.Fatal(err)
			}
		case "Archived":
			// The native migration must preserve flags written by the existing .NET archive workflow.
			if _, err := f.store.Pool.Exec(t.Context(), `UPDATE wishlists SET is_archived=true WHERE id=$1`, list.Id); err != nil {
				t.Fatal(err)
			}
		case "First", "Second", "Newest":
			visible = append(visible, list)
		}
	}
	// Search visibility affects discovery through search, not an already known profile URL.
	current := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Public owner","isVisibleInMemberSearch":false}`, map[string]string{"If-Match": etag}), 200, nil)
	f.access = ""
	// Act
	response := f.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil)
	if response.Header.Get("Cache-Control") != "no-store" {
		t.Fatal("public bearer links must not be cached")
	}
	var body map[string]json.RawMessage
	requireStatus(t, response, 200, &body)
	// Assert: exact public properties, no account details or reservation data.
	if len(body) != 5 || body["displayNameTag"] == nil || string(body["profileImageUrl"]) != "null" || string(body["id"]) != `"`+owner+`"` || string(body["displayName"]) != `"Public owner"` {
		t.Fatal("public identity contract changed")
	}
	var lists []domain.PublicMemberWishlistResponse
	if err := json.Unmarshal(body["wishlists"], &lists); err != nil {
		t.Fatal(err)
	}
	var listProperties []map[string]json.RawMessage
	if err := json.Unmarshal(body["wishlists"], &listProperties); err != nil {
		t.Fatal(err)
	}
	sort.Slice(visible, func(i, j int) bool {
		if !visible[i].CreatedAt.Equal(visible[j].CreatedAt) {
			return visible[i].CreatedAt.After(visible[j].CreatedAt)
		}
		return visible[i].Id < visible[j].Id
	})
	if len(lists) != 3 {
		t.Fatal("profile disclosed non-public lists or lost deterministic order")
	}
	for i, list := range lists {
		if list.Id != visible[i].Id {
			t.Fatal("public lists are not ordered newest first with stable identifier ties")
		}
		if len(listProperties[i]) != 5 || list.Occasion != "birthday" || list.EventDate == nil || *list.EventDate != "2035-01-18" {
			t.Fatal("public wishlist summary contract changed")
		}
		link, err := url.Parse(list.ShareUrl)
		if err != nil || link.Query().Encode() != "" || !strings.HasPrefix(list.ShareUrl, f.service.Config.FrontendOrigin+"/shared-wishlists/") || len(link.Fragment) != 43 {
			t.Fatal("share secret is not confined to the frontend fragment")
		}
		var shared domain.SharedWishlistResponse
		requireStatus(t, f.send(t, "GET", "/api/v1"+link.Path, "", map[string]string{"X-MonKado-Share-Token": link.Fragment}), 200, &shared)
		if shared.Id != list.Id {
			t.Fatal("public profile link opens the wrong list")
		}
	}
	var search domain.PaginatedResponse[domain.UserSearchResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName=Public%20owner", "", nil), 200, &search)
	if search.TotalCount != 0 {
		t.Fatal("profile read changed the member's search preference")
	}
}

func TestPublicMemberProfile_WhenSharingRotatesOrRevokes_UsesCurrentLinksAndVersionedPhoto(t *testing.T) {
	// Arrange
	f := newFixture(t)
	owner := f.member(t, "public-link@example.test")
	access := f.access
	current := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	var photo domain.MemberProfileResponse
	requireStatus(t, f.upload(t, "/api/v1/members/current/profile/image", etag, 42), 200, &photo)
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Shared","occasion":"other"}`, nil), 201, &list)
	profilePath := "/api/v1/members/" + owner + "/profile"
	sharePath := "/api/v1/wishlists/" + list.Id + "/share-link"
	f.access = ""
	var profile domain.PublicMemberProfileResponse
	requireStatus(t, f.send(t, "GET", profilePath, "", nil), 200, &profile)
	if profile.Wishlists == nil || len(profile.Wishlists) != 0 || profile.ProfileImageUrl == nil || *profile.ProfileImageUrl != *photo.ProfileImageUrl {
		t.Fatal("empty lists or versioned photo contract changed")
	}
	image, err := url.Parse(*profile.ProfileImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", image.RequestURI(), "", nil), 200, nil)
	f.access = access
	created := f.send(t, "POST", sharePath, "", nil)
	etag = created.Header.Get("ETag")
	var share domain.WishlistShareLinkResponse
	requireStatus(t, created, 201, &share)
	// Act / Assert
	f.access = ""
	requireStatus(t, f.send(t, "GET", profilePath, "", nil), 200, &profile)
	if len(profile.Wishlists) != 1 || profile.Wishlists[0].ShareUrl != share.ShareUrl || profile.Wishlists[0].EventDate != nil {
		t.Fatal("new sharing was not reflected immediately")
	}
	oldLink, err := url.Parse(share.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	f.access = access
	rotated := f.send(t, "PUT", sharePath, "", map[string]string{"If-Match": etag})
	etag = rotated.Header.Get("ETag")
	requireStatus(t, rotated, 200, &share)
	f.access = ""
	requireStatus(t, f.send(t, "GET", profilePath, "", nil), 200, &profile)
	if len(profile.Wishlists) != 1 || profile.Wishlists[0].ShareUrl != share.ShareUrl || share.ShareUrl == oldLink.String() {
		t.Fatal("profile retained the rotated secret")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1"+oldLink.Path, "", map[string]string{"X-MonKado-Share-Token": oldLink.Fragment}), 404, nil)
	f.access = access
	requireStatus(t, f.send(t, "DELETE", sharePath, "", map[string]string{"If-Match": etag}), 204, nil)
	f.access = ""
	requireStatus(t, f.send(t, "GET", profilePath, "", nil), 200, &profile)
	if profile.Wishlists == nil || len(profile.Wishlists) != 0 {
		t.Fatal("profile retained a revoked list")
	}
}

func TestPublicMemberProfile_WhenMissingUnconfirmedOrDeleted_ReturnsStableNotFound(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "unavailable-profile@example.test")
	f.access = ""
	for _, scenario := range []string{"missing", "unconfirmed", "deleted"} {
		t.Run(scenario, func(t *testing.T) {
			id := member
			switch scenario {
			case "missing":
				id = security.NewID()
			case "unconfirmed":
				// Emulate provider confirmation state through the central fixture.
				if _, err := f.store.Pool.Exec(t.Context(), `UPDATE users SET email_confirmed=false WHERE id=$1`, member); err != nil {
					t.Fatal(err)
				}
			case "deleted":
				if _, err := f.store.Pool.Exec(t.Context(), `DELETE FROM users WHERE id=$1`, member); err != nil {
					t.Fatal(err)
				}
			}
			// Act
			var result domain.ErrorResponse
			requireStatus(t, f.send(t, "GET", "/api/v1/members/"+id+"/profile", "", nil), 404, &result)
			// Assert
			if result.ErrorCode == nil || *result.ErrorCode != domain.AccountPublicProfileNotFound {
				t.Fatal("profile not-found code changed")
			}
		})
	}
}

func TestPublicMemberProfile_WhenQuotaExceededOrCanceled_DoesNotDiscloseData(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.access = ""
	path := "/api/v1/members/" + security.NewID() + "/profile"
	for range 60 {
		requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
	}
	// Act
	response := f.send(t, "GET", path, "", nil)
	if response.Header.Get("Retry-After") != "60" {
		t.Fatal("profile quota is missing retry guidance")
	}
	var result domain.ErrorResponse
	requireStatus(t, response, 429, &result)
	// Assert
	if result.ErrorCode == nil || *result.ErrorCode != domain.RequestRateLimitExceeded {
		t.Fatal("profile quota error contract changed")
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	_, err := f.service.PublicMemberProfile(ctx, security.NewID())
	if !errors.Is(err, context.Canceled) {
		t.Fatal("profile read did not propagate cancellation")
	}
	if result.ValidationErrors != nil {
		t.Fatal("quota error disclosed validation details")
	}
}

func TestPublicMemberProfile_WhenProtectedLinkCorrupt_ReturnsGenericFailureWithoutLoggingContent(t *testing.T) {
	// Arrange
	f := newFixture(t)
	owner := f.member(t, "profile-protection@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Sensitive shared list","occasion":"other"}`, nil), 201, &list)
	var share domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &share)
	link, err := url.Parse(share.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	var logs publicProfileLogCapture
	server := httptest.NewServer(httpapi.New(f.service, slog.New(slog.NewJSONHandler(&logs, nil))))
	t.Cleanup(server.Close)
	originalServer := f.server
	f.server = server
	f.access = ""
	defer func() { f.server = originalServer }()
	// Act / Assert: neither a successful read nor a protection failure logs bearer material.
	requireStatus(t, f.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 200, nil)
	// A corrupted encrypted record represents infrastructure damage, not a user-visible partial profile.
	if _, err := f.store.Pool.Exec(t.Context(), `UPDATE wishlist_share_links SET protected_secret='corrupted-secret' WHERE id=$1`, share.Id); err != nil {
		t.Fatal(err)
	}
	var failure domain.ErrorResponse
	requireStatus(t, f.send(t, "GET", "/api/v1/members/"+owner+"/profile", "", nil), 500, &failure)
	for _, forbidden := range []string{link.Fragment, "Sensitive shared list", "Test member", "profile-protection@example.test", "corrupted-secret"} {
		if strings.Contains(logs.String(), forbidden) {
			t.Fatal("public profile logs disclosed protected content")
		}
		if failure.Message != nil && strings.Contains(*failure.Message, forbidden) {
			t.Fatal("public profile failure disclosed protected content")
		}
	}
}

type publicProfileLogCapture struct {
	mu     sync.Mutex
	buffer bytes.Buffer
}

// Write serializes log capture across request completion goroutines.
func (c *publicProfileLogCapture) Write(value []byte) (int, error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.buffer.Write(value)
}

// String reads a consistent copy while the HTTP logger may still be writing.
func (c *publicProfileLogCapture) String() string {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.buffer.String()
}
