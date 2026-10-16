//go:build integration

package httpapi_test

import (
	"errors"
	"net/http"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestProfile_WhenSearchVisibilityChanges_PersistsPreferenceWithoutHidingPublicPhoto(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "visibility@example.test")
	var identity domain.CurrentSessionResponse
	initial := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := initial.Header.Get("ETag")
	requireStatus(t, initial, 200, &identity)
	if identity.IsVisibleInMemberSearch {
		t.Fatal("new accounts must be hidden from member search by default")
	}
	var photo domain.MemberProfileResponse
	uploaded := f.upload(t, "/api/v1/members/current/profile/image", etag, 42)
	etag = uploaded.Header.Get("ETag")
	requireStatus(t, uploaded, 200, &photo)
	if photo.ProfileImageUrl == nil || photo.IsVisibleInMemberSearch {
		t.Fatal("photo update lost profile visibility")
	}
	photoURL, err := url.Parse(*photo.ProfileImageUrl)
	if err != nil || photoURL.Path != "/api/v1/members/"+member+"/profile/image" {
		t.Fatal("public photo URL missing")
	}
	for _, visible := range []bool{false, true} {
		// Act
		var profile domain.MemberProfileResponse
		updated := f.send(t, "PUT", "/api/v1/members/current/profile", jsonInput(t, map[string]any{"displayName": "Visibility member", "isVisibleInMemberSearch": visible}), map[string]string{"If-Match": etag})
		next := updated.Header.Get("ETag")
		requireStatus(t, updated, 200, &profile)
		// Assert
		if profile.IsVisibleInMemberSearch != visible || next == etag || next == "" {
			t.Fatal("profile visibility or version was not updated")
		}
		requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Stale","isVisibleInMemberSearch":true}`, map[string]string{"If-Match": etag}), 412, nil)
		etag = next
		requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &identity)
		if identity.IsVisibleInMemberSearch != visible {
			t.Fatal("session lost the saved visibility preference")
		}
		var found domain.PaginatedResponse[domain.UserSearchResponse]
		requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName=Visibility", "", nil), 200, &found)
		if (found.TotalCount == 1) != visible || (len(found.Items) == 1) != visible {
			t.Fatal("member search did not apply visibility to items and metadata")
		}
		access := f.access
		f.access = ""
		requireStatus(t, f.send(t, "GET", photoURL.RequestURI(), "", nil), 200, nil)
		f.access = access
		// Omitted preference must preserve the saved value rather than restoring true.
		nameOnly := f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Visibility member"}`, map[string]string{"If-Match": etag})
		etag = nameOnly.Header.Get("ETag")
		requireStatus(t, nameOnly, 200, &profile)
		if profile.IsVisibleInMemberSearch != visible {
			t.Fatal("display-name change overwrote the search preference")
		}
	}
	for _, invalid := range []string{`"false"`, `1`, `null`} {
		requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Visibility member","isVisibleInMemberSearch":`+invalid+`}`, map[string]string{"If-Match": etag}), 400, nil)
	}
	for _, payload := range []string{`{"displayName":"Visibility member"}`, `{"displayName":"Visibility member","isVisibleInMemberSearch":true}`} {
		unchanged := f.send(t, "PUT", "/api/v1/members/current/profile", payload, map[string]string{"If-Match": etag})
		if unchanged.Header.Get("ETag") != etag {
			t.Fatal("unchanged member profile changed its version")
		}
		requireStatus(t, unchanged, 200, nil)
	}
}

func TestLogin_WhenAccountLocked_CannotClearLockoutWithAnotherAttempt(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "locked@example.test")
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	f.access = ""
	f.csrfToken(t)
	valid := `{"email":"locked@example.test","password":"A long test password 🎁"}`
	// Act
	for range 5 {
		requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", `{"email":"locked@example.test","password":"wrong password"}`, nil), 401, nil)
	}
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", valid, nil), 401, nil)
	now = now.Add(4 * time.Minute)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", valid, nil), 401, nil)
	now = now.Add(2 * time.Minute)
	var tokens domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", valid, nil), 200, &tokens)
	// Assert
	if tokens.AccessToken == "" {
		t.Fatal("credentials did not recover after lockout expiry")
	}
}

func TestRefresh_WhenRotated_RejectsReplayAndRespectsSessionLifetime(t *testing.T) {
	for _, persistent := range []bool{false, true} {
		t.Run(map[bool]string{false: "sliding", true: "persistent"}[persistent], func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			f.member(t, "refresh@example.test")
			now := time.Now().UTC()
			f.service.Now = func() time.Time { return now }
			f.access = ""
			f.csrfToken(t)
			login := jsonInput(t, map[string]any{"email": "refresh@example.test", "password": "A long test password 🎁", "rememberMe": persistent})
			r := f.send(t, "POST", "/api/v1/auth/sessions", login, nil)
			var oldRefresh *http.Cookie
			for _, cookie := range r.Cookies() {
				if strings.HasSuffix(cookie.Name, ".Refresh") {
					oldRefresh = cookie
				}
			}
			var original domain.AccessTokenResponse
			requireStatus(t, r, 200, &original)
			if oldRefresh == nil {
				t.Fatal("refresh cookie missing")
			}
			originalExpiry := oldRefresh.Expires
			// Act
			now = now.Add(16 * time.Minute)
			f.access = original.AccessToken
			requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
			f.access = ""
			f.csrfToken(t)
			refreshed := f.send(t, "POST", "/api/v1/auth/sessions/refresh", "", nil)
			var newRefresh *http.Cookie
			for _, cookie := range refreshed.Cookies() {
				if strings.HasSuffix(cookie.Name, ".Refresh") {
					newRefresh = cookie
				}
			}
			var renewed domain.AccessTokenResponse
			requireStatus(t, refreshed, 200, &renewed)
			_, replayErr := f.service.Refresh(t.Context(), oldRefresh.Value)
			// Assert
			var replay *domain.Error
			if newRefresh == nil || newRefresh.Value == oldRefresh.Value || !errors.As(replayErr, &replay) || replay.Status != 401 || renewed.AccessToken == original.AccessToken {
				t.Fatal("refresh rotation or replay protection differs")
			}
			if persistent && newRefresh.Expires != originalExpiry || !persistent && !newRefresh.Expires.IsZero() {
				t.Fatal("browser lifetime contract differs")
			}
			f.access = renewed.AccessToken
			requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, nil)
			f.csrfToken(t)
			requireStatus(t, f.send(t, "DELETE", "/api/v1/auth/sessions/current", "", nil), 204, nil)
			requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
		})
	}
}

func TestPasswordReset_WhenProofUsed_RevokesSessionsAndRejectsReplay(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "reset@example.test")
	oldAccess := f.access
	f.access = ""
	f.csrfToken(t)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/password-reset-requests", `{"email":"unknown@example.test"}`, nil), 202, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/password-reset-requests", `{"email":"reset@example.test"}`, nil), 202, nil)
	var id string
	if err := f.store.Pool.QueryRow(t.Context(), `SELECT id::text FROM authentication_email_outbox WHERE user_id=$1 AND kind='PASSWORD_RESET'`, member).Scan(&id); err != nil {
		t.Fatal(err)
	}
	message, err := f.service.PrepareAuthenticationEmail(t.Context(), id)
	if err != nil {
		t.Fatal(err)
	}
	link, err := url.Parse(message.ActionURL)
	if err != nil {
		t.Fatal(err)
	}
	newPassword := "A different long password 🎁"
	reset := jsonInput(t, map[string]string{"userId": member, "token": actionCredentials(t, link).Get("token"), "newPassword": newPassword})
	// Act
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/password-resets", reset, nil), 204, nil)
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/password-resets", reset, nil), 400, nil)
	f.access = oldAccess
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
	f.access = ""
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", `{"email":"reset@example.test","password":"A long test password 🎁"}`, nil), 401, nil)
	var tokens domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", jsonInput(t, map[string]string{"email": "reset@example.test", "password": newPassword}), nil), 200, &tokens)
	f.access = tokens.AccessToken
	// Assert
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/password", jsonInput(t, map[string]string{"currentPassword": newPassword, "newPassword": newPassword}), nil), 400, nil)
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/password", jsonInput(t, map[string]string{"currentPassword": "incorrect password", "newPassword": "Next long test password 🎁"}), nil), 400, nil)
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/password", jsonInput(t, map[string]string{"currentPassword": newPassword, "newPassword": "Next long test password 🎁"}), nil), 204, nil)
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 401, nil)
}

func TestMembers_WhenProfileChanges_SearchesConfirmedNamesWithoutAccentSensitivity(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "profile@example.test")
	current := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	version := current.Header.Get("ETag")
	requireStatus(t, current, 200, nil)
	// Act
	updated := f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":" Élodie 🎁 ","isVisibleInMemberSearch":true}`, map[string]string{"If-Match": version})
	nextVersion := updated.Header.Get("ETag")
	var profile domain.MemberProfileResponse
	requireStatus(t, updated, 200, &profile)
	requireStatus(t, f.send(t, "PUT", "/api/v1/members/current/profile", `{"displayName":"Other"}`, map[string]string{"If-Match": version}), 412, nil)
	var found domain.PaginatedResponse[domain.UserSearchResponse]
	requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName=ELODIE", "", nil), 200, &found)
	// Assert
	if profile.DisplayName != "Élodie 🎁" || nextVersion == version || len(found.Items) != 1 || found.Items[0].Id != member {
		t.Fatal("profile update or normalized search differs")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName=ELODIE&page=100", "", nil), 200, &found)
	if len(found.Items) != 0 || found.TotalCount != 1 {
		t.Fatal("out-of-range search metadata differs")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName=no-match", "", nil), 200, &found)
	if len(found.Items) != 0 || found.TotalPages != 0 {
		t.Fatal("empty search metadata differs")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/members?displayName=x&pageSize=0", "", nil), 400, nil)
}
