//go:build integration

package httpapi_test

import (
	"encoding/json"
	"fmt"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
)

func TestGroupWishlist_WhenOwnerReads_RespectsSurpriseModeWithoutAllowingGroupReservations(t *testing.T) {
	for _, surprise := range []bool{true, false} {
		t.Run(fmt.Sprintf("surprise_%t", surprise), func(t *testing.T) {
			// Arrange: the list belongs to an ordinary group member, not the organizer.
			s := newGroupScenario(t)
			s.accept(t, s.member, s.memberID)
			var list domain.WishlistResponse
			requireStatus(t, s.member.send(t, "POST", "/api/v1/wishlists", fmt.Sprintf(`{"name":"Own shared list","occasion":"other","surpriseMode":%t}`, surprise), nil), 201, &list)
			own := "/api/v1/wishlists/" + list.Id
			base := s.base + "/wishlists/" + list.Id
			wishes := []domain.WishResponse{}
			for i := range 3 {
				var wish domain.WishResponse
				created := s.member.send(t, "POST", own+"/wishes", fmt.Sprintf(`{"name":"Gift %d","quantity":2}`, i), nil)
				tag := created.Header.Get("ETag")
				requireStatus(t, created, 201, &wish)
				if i == 0 {
					requireStatus(t, s.member.upload(t, own+"/wishes/"+wish.Id+"/image", tag, 42), 200, &wish)
				}
				wishes = append(wishes, wish)
			}
			requireStatus(t, s.member.send(t, "PUT", base, "", nil), 201, nil)
			for i, quantity := range []int{2, 1} {
				requireStatus(t, s.owner.send(t, "PUT", base+"/wishes/"+wishes[i].Id+"/reservations/current", fmt.Sprintf(`{"quantity":%d}`, quantity), nil), 201, nil)
			}
			var link domain.WishlistShareLinkResponse
			requireStatus(t, s.member.send(t, "POST", own+"/share-link", "", nil), 201, &link)
			headers := map[string]string{"X-MonKado-Share-Token": strings.Split(link.ShareUrl, "#")[1]}
			public := "/api/v1/shared-wishlists/" + link.Id
			var groupImageGrant string
			// Act / Assert: both group and authenticated public reads preserve the same privacy.
			for _, path := range []string{base, public} {
				var read domain.SharedWishlistResponse
				requireStatus(t, s.member.send(t, "GET", path, "", headers), 200, &read)
				if read.CanReserve || read.CurrentParticipant != nil || len(read.Wishes) != 3 {
					t.Fatal("owner read is not a complete read-only list")
				}
				for i, wish := range read.Wishes {
					assertOwnerReservationQuantities(t, surprise, wish.ReservedQuantity, wish.AvailableQuantity, wish.CurrentParticipantReservedQuantity, max(0, 2-i))
				}
				requireStatus(t, s.member.send(t, "GET", path+"?availableOnly=true", "", headers), 200, &read)
				want := 2
				if surprise {
					want = 3
				}
				if len(read.Wishes) != want {
					t.Fatal("availability filtering leaked or ignored reservation state")
				}
				for i, wish := range wishes {
					var detail map[string]json.RawMessage
					requireStatus(t, s.member.send(t, "GET", path+"/wishes/"+wish.Id, "", headers), 200, &detail)
					if string(detail["canReserve"]) != "false" {
						t.Fatal("owner detail allows reservation")
					}
					for _, field := range []string{"reservedQuantity", "availableQuantity", "currentParticipantReservedQuantity"} {
						if _, exists := detail[field]; !exists {
							t.Fatalf("missing nullable field %s", field)
						}
					}
					var projected domain.SharedWishDetailResponse
					data, err := json.Marshal(detail)
					if err != nil {
						t.Fatal(err)
					}
					if err = json.Unmarshal(data, &projected); err != nil {
						t.Fatal(err)
					}
					assertOwnerReservationQuantities(t, surprise, projected.ReservedQuantity, projected.AvailableQuantity, projected.CurrentParticipantReservedQuantity, max(0, 2-i))
					if path == base && i == 0 {
						if projected.ImageUrl == nil {
							t.Fatal("owner group read lost the wish image")
						}
						grant, err := url.Parse(*projected.ImageUrl)
						if err != nil {
							t.Fatal(err)
						}
						groupImageGrant = grant.RequestURI()
						requireStatus(t, s.member.send(t, "GET", groupImageGrant, "", nil), 200, nil)
					}
				}
			}
			// Peers retain reservation state; reading does not enroll the list owner.
			var peer domain.SharedWishlistResponse
			requireStatus(t, s.owner.send(t, "GET", base, "", nil), 200, &peer)
			if !peer.CanReserve || peer.CurrentParticipant == nil || *peer.Wishes[0].ReservedQuantity != 2 || *peer.Wishes[1].AvailableQuantity != 1 {
				t.Fatal("owner privacy changed another member's reservation view")
			}
			var participants int
			if err := s.member.store.Pool.QueryRow(t.Context(), `SELECT count(*) FROM wishlist_participants WHERE wishlist_id=$1 AND member_id=$2`, list.Id, s.memberID).Scan(&participants); err != nil {
				t.Fatal(err)
			}
			if participants != 0 {
				t.Fatal("owner reading created a participant")
			}
			// A fresh group detail immediately applies a changed owner setting.
			readOwn := s.member.send(t, "GET", own, "", nil)
			tag := readOwn.Header.Get("ETag")
			requireStatus(t, readOwn, 200, nil)
			requireStatus(t, s.member.send(t, "PUT", own, fmt.Sprintf(`{"name":"Own shared list","occasion":"other","surpriseMode":%t}`, !surprise), map[string]string{"If-Match": tag}), 200, nil)
			var changed domain.SharedWishDetailResponse
			requireStatus(t, s.member.send(t, "GET", base+"/wishes/"+wishes[0].Id, "", nil), 200, &changed)
			assertOwnerReservationQuantities(t, !surprise, changed.ReservedQuantity, changed.AvailableQuantity, changed.CurrentParticipantReservedQuantity, 2)
			reservation := base + "/wishes/" + wishes[2].Id + "/reservations/current"
			for _, method := range []string{"GET", "PUT", "DELETE"} {
				body := ""
				if method == "PUT" {
					body = `{"quantity":1}`
				}
				var failure domain.ErrorResponse
				requireStatus(t, s.member.send(t, method, reservation, body, map[string]string{"If-Match": `"00000001"`}), 409, &failure)
				if failure.ErrorCode == nil || *failure.ErrorCode != domain.GroupOwnerWishlist {
					t.Fatal("owner group reservation protection changed")
				}
			}
			requireStatus(t, s.member.send(t, "GET", base+"/wishes/"+security.NewID(), "", nil), 404, nil)
			requireStatus(t, s.foreign.send(t, "GET", base, "", nil), 404, nil)
			// Removing group visibility also revokes owner reads in that group, without deleting the owned list.
			requireStatus(t, s.member.send(t, "DELETE", base, "", nil), 204, nil)
			requireStatus(t, s.member.send(t, "GET", base, "", nil), 404, nil)
			requireStatus(t, s.member.send(t, "GET", groupImageGrant, "", nil), 404, nil)
			requireStatus(t, s.member.send(t, "GET", own, "", nil), 200, nil)
		})
	}
}

func assertOwnerReservationQuantities(t *testing.T, surprise bool, reserved, available, current *int, expected int) {
	t.Helper()
	if current != nil {
		t.Fatal("owner read disclosed a participant's personal quantity")
	}
	if surprise {
		if reserved != nil || available != nil {
			t.Fatal("surprise mode disclosed reservations")
		}
		return
	}
	if reserved == nil || available == nil || *reserved != expected || *available != 2-expected {
		t.Fatal("disabled surprise mode did not retain aggregate quantities")
	}
}

func TestWishlist_WhenSurpriseModeIsSaved_PersistsDefaultsAndConcurrency(t *testing.T) {
	for _, mode := range []string{"", `,"surpriseMode":null`, `,"surpriseMode":true`, `,"surpriseMode":false`} {
		t.Run(url.QueryEscape(mode), func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			f.member(t, "surprise-mode@example.test")
			var list domain.WishlistResponse
			created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"Surprise","occasion":"other"`+mode+`}`, nil)
			tag := created.Header.Get("ETag")
			requireStatus(t, created, 201, &list)
			want := mode != `,"surpriseMode":false`
			if list.SurpriseMode != want {
				t.Fatal("wrong default or explicit mode")
			}
			path := "/api/v1/wishlists/" + list.Id
			// Act / Assert: absent and null preserve a saved mode on update.
			for _, suffix := range []string{"", `,"surpriseMode":null`} {
				updated := f.send(t, "PUT", path, `{"name":"Renamed","occasion":"other"`+suffix+`}`, map[string]string{"If-Match": tag})
				tag = updated.Header.Get("ETag")
				requireStatus(t, updated, 200, &list)
				if list.SurpriseMode != want {
					t.Fatal("metadata update reset surprise mode")
				}
			}
			body := fmt.Sprintf(`{"name":"Renamed","occasion":"other","surpriseMode":%t}`, !want)
			updated := f.send(t, "PUT", path, body, map[string]string{"If-Match": tag})
			requireStatus(t, updated, 200, &list)
			if list.SurpriseMode == want {
				t.Fatal("mode update was not applied")
			}
			requireStatus(t, f.send(t, "PUT", path, body, map[string]string{"If-Match": tag}), 412, nil)
			requireStatus(t, f.send(t, "GET", path, "", nil), 200, &list)
			if list.SurpriseMode == want {
				t.Fatal("saved mode did not survive a fresh read")
			}
			var lists []domain.WishlistResponse
			requireStatus(t, f.send(t, "GET", "/api/v1/wishlists", "", nil), 200, &lists)
			if len(lists) != 1 || lists[0].SurpriseMode != list.SurpriseMode {
				t.Fatal("overview lost the saved mode")
			}
			for _, value := range []string{`"false"`, `0`, `{}`} {
				requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Invalid","occasion":"other","surpriseMode":`+value+`}`, nil), 400, nil)
			}
		})
	}
}
