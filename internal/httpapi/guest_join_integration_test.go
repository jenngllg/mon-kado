//go:build integration

package httpapi_test

import (
	"encoding/json"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func sharedJoinFixture(t *testing.T) (*fixture, string, map[string]string) {
	t.Helper()
	f := newFixture(t)
	f.member(t, "join-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Join fixture","occasion":"other"}`, nil), 201, &list)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	parsed, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	return f, "/api/v1/shared-wishlists/" + link.Id + "/participants", map[string]string{"X-MonKado-Share-Token": parsed.Fragment}
}

func TestGuestJoin_WhenDisplayNameIsMissingBlankOrInvalid_RejectsWithoutCreatingGuestIdentity(t *testing.T) {
	// Arrange
	f, path, proof := sharedJoinFixture(t)
	for _, input := range []struct{ name, body string }{
		{"emptyBody", ""}, {"missing", `{}`}, {"nullBody", `null`}, {"nullName", `{"displayName":null}`},
		{"emptyName", `{"displayName":""}`}, {"spaces", `{"displayName":"   "}`},
		{"unicodeWhitespace", `{"displayName":"\u00a0\u2003"}`}, {"control", `{"displayName":"Guest\nname"}`},
		{"tooLong", `{"displayName":"` + strings.Repeat("🎁", 81) + `"}`},
	} {
		t.Run(input.name, func(t *testing.T) {
			visitor := f.visitor(t)
			// Act
			var failure domain.ErrorResponse
			requireStatus(t, visitor.send(t, "POST", path, input.body, proof), 400, &failure)
			// Assert: public identity remains absent, even after the rejected mutation.
			if failure.ErrorCode == nil || *failure.ErrorCode != domain.RequestValidationError || len(failure.ValidationErrors) != 1 || failure.ValidationErrors[0].PropertyName != "displayName" {
				t.Fatalf("unexpected validation contract: %#v", failure)
			}
			requireStatus(t, visitor.send(t, "GET", path+"/current", "", proof), 401, nil)
		})
	}
}

func TestGuestJoin_WhenUnicodeNameHasEightyScalars_TrimsAndPreservesExistingParticipation(t *testing.T) {
	// Arrange
	f, path, proof := sharedJoinFixture(t)
	visitor := f.visitor(t)
	name := strings.Repeat("🎁", 80)
	padded := "  " + name + "  "
	content, err := json.Marshal(domain.JoinSharedWishlistRequest{DisplayName: &padded})
	if err != nil {
		t.Fatal(err)
	}
	// Act
	response := visitor.send(t, "POST", path, string(content), proof)
	var first, repeated, current domain.WishlistParticipantResponse
	if response.Header.Get("Location") != path+"/current" || response.Header.Get("ETag") != "" {
		t.Fatal("participant creation headers changed")
	}
	requireStatus(t, response, 201, &first)
	requireStatus(t, visitor.send(t, "POST", path, `{"displayName":"Another supplied name"}`, proof), 200, &repeated)
	requireStatus(t, visitor.send(t, "GET", path+"/current", "", proof), 200, &current)
	// Assert
	if first.Id == "" || first.DisplayName != name || repeated != first || current != first {
		t.Fatal("guest identity or immutable participant name changed")
	}
}

func TestMemberJoin_WhenGuestDetailsAreOmittedOrInvalid_UsesLiveProfileAndExistingParticipation(t *testing.T) {
	// Arrange
	f, path, proof := sharedJoinFixture(t)
	member := f.visitor(t)
	member.member(t, "joining-member@example.test")
	var initial domain.WishlistParticipantResponse
	for index, body := range []string{"", "null", `{}`, `{"displayName":"Guest\nname"}`, `{"displayName":"` + strings.Repeat("🎁", 81) + `"}`} {
		// Act
		var participant domain.WishlistParticipantResponse
		status := 200
		if index == 0 {
			status = 201
		}
		requireStatus(t, member.send(t, "POST", path, body, proof), status, &participant)
		// Assert
		if participant.DisplayName != "Test member" || participant.Id == "" {
			t.Fatal("guest details affected identified participation")
		}
		if index == 0 {
			initial = participant
		} else if participant != initial {
			t.Fatal("joining twice created or changed participation")
		}
	}
}
