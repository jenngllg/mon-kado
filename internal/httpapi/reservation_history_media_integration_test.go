//go:build integration

package httpapi_test

import (
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestReservationHistory_WhenSourceIsAccessible_ExposesAuthorizedMediaAndNavigation(t *testing.T) {
	for _, owned := range []bool{true, false} {
		t.Run(map[bool]string{true: "privateOwner", false: "sharedMember"}[owned], func(t *testing.T) {
			// Arrange
			owner := newFixture(t)
			ownerID := owner.member(t, "history-owner@example.test")
			var list domain.WishlistResponse
			requireStatus(t, owner.send(t, "POST", "/api/v1/wishlists", `{"name":"History","occasion":"other"}`, nil), 201, &list)
			base := "/api/v1/wishlists/" + list.Id
			var wish domain.WishResponse
			created := owner.send(t, "POST", base+"/wishes", `{"name":"Wish","quantity":2}`, nil)
			wishTag := created.Header.Get("ETag")
			requireStatus(t, created, 201, &wish)
			uploaded := owner.upload(t, base+"/wishes/"+wish.Id+"/image", wishTag, 42)
			wishTag = uploaded.Header.Get("ETag")
			requireStatus(t, uploaded, 200, &wish)
			reader := owner
			reservationPath := base + "/wishes/" + wish.Id + "/reservations/current"
			var headers map[string]string
			var link domain.WishlistShareLinkResponse
			var linkTag string
			if !owned {
				shared := owner.send(t, "POST", base+"/share-link", "", nil)
				linkTag = shared.Header.Get("ETag")
				requireStatus(t, shared, 201, &link)
				parsed, err := url.Parse(link.ShareUrl)
				if err != nil {
					t.Fatal("invalid fixture sharing URL")
				}
				headers = map[string]string{"X-MonKado-Share-Token": parsed.Fragment}
				reader = owner.visitor(t)
				reader.member(t, "history-reader@example.test")
				sharedPath := "/api/v1/shared-wishlists/" + link.Id
				requireStatus(t, reader.send(t, "POST", sharedPath+"/participants", "", headers), 201, nil)
				reservationPath = sharedPath + "/wishes/" + wish.Id + "/reservations/current"
			}
			saved := reader.send(t, "PUT", reservationPath, `{"quantity":1}`, headers)
			reservationTag := saved.Header.Get("ETag")
			requireStatus(t, saved, 201, nil)
			// Act
			var history domain.PaginatedResponse[domain.GiftReservationHistoryResponse]
			requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			// Assert
			if len(history.Items) != 1 {
				t.Fatal("history must contain only this member's reservation")
			}
			item := history.Items[0]
			if item.OwnerId == nil || *item.OwnerId != ownerID || item.OwnerDisplayName == nil || item.ImageUrl == nil {
				t.Fatal("accessible source is missing its owner or media")
			}
			if item.InaccessibilityReason != nil {
				t.Fatal("accessible source must not invent an access restriction")
			}
			imageURL, err := url.Parse(*item.ImageUrl)
			if err != nil {
				t.Fatal("invalid image grant URL")
			}
			if owned {
				if item.OwnedWishPath == nil || *item.OwnedWishPath != "/lists/"+list.Id+"/wishes/"+wish.Id || item.ShareUrl != nil || item.ShareLinkId != nil {
					t.Fatal("private owner history must not publish a share link")
				}
				if !strings.HasPrefix(imageURL.Path, base+"/wishes/") {
					t.Fatal("private image grant uses the wrong resource")
				}
			} else if item.OwnedWishPath != nil || item.ShareUrl == nil || *item.ShareUrl != link.ShareUrl {
				t.Fatal("shared history has incorrect navigation")
			}
			requireStatus(t, reader.send(t, "GET", imageURL.RequestURI(), "", nil), 200, nil)
			// Cancelled history may still show the current accessible source image.
			if headers == nil {
				headers = map[string]string{}
			}
			headers["If-Match"] = reservationTag
			requireStatus(t, reader.send(t, "DELETE", reservationPath, "", headers), 204, nil)
			requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			if history.Items[0].ImageUrl == nil || history.Items[0].Status != "cancelled" {
				t.Fatal("cancellation lost the accessible image or lifecycle")
			}
			// Archival temporarily removes grants while retaining the cancelled history.
			listRead := owner.send(t, "GET", base, "", nil)
			listVersion := listRead.Header.Get("ETag")
			requireStatus(t, listRead, 200, nil)
			archived := owner.send(t, "PATCH", base, `{"isArchived":true}`, map[string]string{"If-Match": listVersion})
			listVersion = archived.Header.Get("ETag")
			requireStatus(t, archived, 200, nil)
			requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			item = history.Items[0]
			if item.InaccessibilityReason == nil || *item.InaccessibilityReason != "archived" || !item.IsArchived || item.ImageUrl != nil || item.ShareUrl != nil || item.OwnedWishPath != nil || item.OwnerId == nil || item.Status != "cancelled" {
				t.Fatal("archival retained access or lost the cancelled lifecycle")
			}
			imageStatus := 404
			if owned {
				// Existing owner image grants remain usable for read-only archived content.
				imageStatus = 200
			}
			requireStatus(t, reader.send(t, "GET", imageURL.RequestURI(), "", nil), imageStatus, nil)
			requireStatus(t, owner.send(t, "PATCH", base, `{"isArchived":false}`, map[string]string{"If-Match": listVersion}), 200, nil)
			requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			if history.Items[0].InaccessibilityReason != nil || history.Items[0].ImageUrl == nil {
				t.Fatal("unarchiving did not restore current authorized media")
			}
			// Removing the source/access must remove grants, not resurrect historical access.
			if owned {
				requireStatus(t, owner.send(t, "DELETE", base+"/wishes/"+wish.Id, "", map[string]string{"If-Match": wishTag}), 204, nil)
			} else {
				requireStatus(t, owner.send(t, "DELETE", base+"/share-link", "", map[string]string{"If-Match": linkTag}), 204, nil)
			}
			requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			item = history.Items[0]
			if item.ImageUrl != nil || item.ShareUrl != nil || item.OwnedWishPath != nil || item.OwnerId != nil {
				t.Fatal("inaccessible history retained live source access")
			}
			expectedReason := "notShared"
			if owned {
				expectedReason = "wishDeleted"
			}
			if item.InaccessibilityReason == nil || *item.InaccessibilityReason != expectedReason || item.Status != "cancelled" {
				t.Fatal("source restriction must preserve the cancelled lifecycle and expose its known cause")
			}
			requireStatus(t, reader.send(t, "GET", imageURL.RequestURI(), "", nil), 404, nil)
			if !owned {
				requireStatus(t, owner.send(t, "DELETE", base+"/wishes/"+wish.Id, "", map[string]string{"If-Match": wishTag}), 204, nil)
				requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
				if history.Items[0].InaccessibilityReason == nil || *history.Items[0].InaccessibilityReason != "notShared" {
					t.Fatal("revoked sharing must mask later private wish deletion")
				}
			}
			freshList := owner.send(t, "GET", base, "", nil)
			listTag := freshList.Header.Get("ETag")
			requireStatus(t, freshList, 200, nil)
			requireStatus(t, owner.send(t, "DELETE", base, "", map[string]string{"If-Match": listTag}), 204, nil)
			requireStatus(t, reader.send(t, "GET", "/api/v1/members/current/reservations", "", nil), 200, &history)
			if history.Items[0].InaccessibilityReason == nil || *history.Items[0].InaccessibilityReason != "wishlistDeleted" {
				t.Fatal("deleted list must have its distinct history cause")
			}
		})
	}
}
