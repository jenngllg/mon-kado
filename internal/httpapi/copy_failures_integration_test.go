//go:build integration

package httpapi_test

import (
	"context"
	"os"
	"path/filepath"
	"sync"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestWishCopies_WhenSourceImageIsMissingOrCorrupt_LeavesDestinationAndFilesUnchanged(t *testing.T) {
	for _, failure := range []string{"missing", "corrupt"} {
		t.Run(failure, func(t *testing.T) {
			// Arrange
			f := newFixture(t)
			f.member(t, "copy-image-failure@example.test")
			var source, destination domain.WishlistResponse
			requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Source","occasion":"other"}`, nil), 201, &source)
			requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Destination","occasion":"other"}`, nil), 201, &destination)
			var wish domain.WishResponse
			base := "/api/v1/wishlists/" + source.Id + "/wishes"
			created := f.send(t, "POST", base, `{"name":"Illustrated gift"}`, nil)
			version := created.Header.Get("ETag")
			requireStatus(t, created, 201, &wish)
			path := base + "/" + wish.Id
			requireStatus(t, f.upload(t, path+"/image", version, 71), 200, nil)
			pictures, err := filepath.Glob(filepath.Join(f.service.Config.ImagePath, "*.webp"))
			if err != nil || len(pictures) != 1 {
				t.Fatal("expected exactly one uploaded fixture")
			}
			if failure == "missing" {
				err = os.Remove(pictures[0])
			} else {
				err = os.WriteFile(pictures[0], []byte("corrupt normalized bytes"), 0600)
			}
			if err != nil {
				t.Fatal(err)
			}
			before, err := os.ReadDir(f.service.Config.ImagePath)
			if err != nil {
				t.Fatal(err)
			}
			// Act
			var response domain.ErrorResponse
			requireStatus(t, f.send(t, "POST", path+"/copies", `{"destinationWishlistId":"`+destination.Id+`"}`, nil), 503, &response)
			// Assert through public reads, and filesystem effects.
			if response.ErrorCode == nil || *response.ErrorCode != domain.TechnicalServiceUnavailable {
				t.Fatal("image failure lost its stable error code")
			}
			var collection domain.WishCollectionResponse
			requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+destination.Id+"/wishes", "", nil), 200, &collection)
			if collection.Wishes == nil || len(collection.Wishes) != 0 {
				t.Fatal("failed copy left a destination wish")
			}
			after, err := os.ReadDir(f.service.Config.ImagePath)
			if err != nil || len(after) != len(before) {
				t.Fatal("failed copy left staged files")
			}
		})
	}
}

func TestWishCopies_WhenParentsAreReciprocal_CompletesBothWithoutDeadlocking(t *testing.T) {
	// Arrange
	f := newFixture(t)
	owner := f.member(t, "reciprocal-copy@example.test")
	var lists [2]domain.WishlistResponse
	var wishes [2]domain.WishResponse
	for index, name := range []string{"First", "Second"} {
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"`+name+`","occasion":"other"}`, nil), 201, &lists[index])
		requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+lists[index].Id+"/wishes", `{"name":"`+name+` gift"}`, nil), 201, &wishes[index])
	}
	ctx, cancel := context.WithTimeout(t.Context(), 5*time.Second)
	defer cancel()
	start := make(chan struct{})
	results := make(chan error, 2)
	var ready sync.WaitGroup
	ready.Add(2)
	// Act: synchronized callers exercise the public service contract with real PostgreSQL locks.
	for index := range lists {
		go func() {
			ready.Done()
			<-start
			_, _, err := f.service.CopyWish(ctx, owner, lists[1-index].Id, lists[index].Id, wishes[index].Id, "", "")
			results <- err
		}()
	}
	ready.Wait()
	close(start)
	// Assert
	for range 2 {
		if err := <-results; err != nil {
			t.Fatal(err)
		}
	}
	for _, list := range lists {
		var collection domain.WishCollectionResponse
		requireStatus(t, f.send(t, "GET", "/api/v1/wishlists/"+list.Id+"/wishes", "", nil), 200, &collection)
		if len(collection.Wishes) != 2 || collection.Wishes[0].Position >= collection.Wishes[1].Position {
			t.Fatal("reciprocal copies lost content or sequence ordering")
		}
	}
}
