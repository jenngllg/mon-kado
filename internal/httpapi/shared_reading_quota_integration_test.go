//go:build integration

package httpapi_test

import (
	"encoding/json"
	"net/url"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestSharedReading_WhenGalleryImagesReload_PreservesDetailAndReservationAccess(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "gallery-owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Gallery","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	path := "/api/v1/wishlists/" + list.Id + "/wishes"
	created := f.send(t, "POST", path, `{"name":"Photo"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	requireStatus(t, f.upload(t, path+"/"+wish.Id+"/image", etag, 42), 200, &wish)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	share, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": share.Fragment}
	visitor := f.visitor(t)
	base := "/api/v1/shared-wishlists/" + link.Id
	var detail domain.SharedWishDetailResponse
	requireStatus(t, visitor.send(t, "GET", base+"/wishes/"+wish.Id, "", headers), 200, &detail)
	image, err := url.Parse(*detail.ImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	// Act: three 21-photo gallery renders use 63 valid image reads in one window.
	for range 3 {
		requireStatus(t, visitor.send(t, "GET", base, "", headers), 200, nil)
		for range 21 {
			requireStatus(t, visitor.send(t, "GET", image.RequestURI(), "", nil), 200, nil)
		}
	}
	// Assert: reads did not enroll the visitor or reserve anything.
	requireStatus(t, visitor.send(t, "GET", base+"/participants/current", "", headers), 401, nil)
	requireStatus(t, visitor.send(t, "GET", base+"/wishes/"+wish.Id, "", headers), 200, nil)
	requireStatus(t, visitor.send(t, "POST", base+"/participants", `{"displayName":"Visitor"}`, headers), 201, nil)
	reservation := base + "/wishes/" + wish.Id + "/reservations/current"
	requireStatus(t, visitor.send(t, "PUT", reservation, `{"quantity":1}`, headers), 201, nil)
	var saved domain.GiftReservationResponse
	requireStatus(t, visitor.send(t, "GET", reservation, "", headers), 200, &saved)
	if saved.Quantity != 1 {
		t.Fatal("reservation was not persisted")
	}
}

func TestSharedReading_WhenEndpointQuotaIsReached_PreservesWriteQuotaAndRecordsMemberError(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "read-quota@example.test")
	now := time.Now().UTC()
	f.service.Now = func() time.Time { return now }
	missing := "/api/v1/shared-wishlists/01950000-0000-7000-8000-000000000001"
	// Act
	for range 120 {
		requireStatus(t, f.send(t, "GET", missing, "", nil), 404, nil)
	}
	rejected := f.send(t, "GET", missing, "", nil)
	correlation := rejected.Header.Get("X-Correlation-ID")
	requireStatus(t, rejected, 429, nil)
	// The participation endpoint still reaches its own resource validation.
	requireStatus(t, f.send(t, "POST", missing+"/participants", "", nil), 404, nil)
	f.administrator(t, "read-quota-admin@example.test")
	var page domain.PaginatedResponse[application.ActivityEvent]
	requireStatus(t, f.send(t, "GET", "/api/v1/admin/member-activity-events?logLevel=error", "", nil), 200, &page)
	// Assert: optional 404 reads stay quiet; the technical read failure is visible.
	found := false
	for _, event := range page.Items {
		if event.MemberID != member {
			continue
		}
		var payload map[string]any
		if json.Unmarshal(event.Payload, &payload) == nil && payload["correlationId"] == correlation {
			found = payload["errorCode"] == domain.RequestRateLimitExceeded && payload["statusCode"] == float64(429)
		}
	}
	if !found {
		t.Fatal("member throttling failure is absent from administrator history")
	}
}
