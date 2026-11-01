//go:build integration

package httpapi_test

import (
	"bytes"
	"image"
	"io"
	"net/url"
	"strconv"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestSharePreviews_WhenPublic_ReturnsOnlyEscapedTitleAndBoundedCurrentImages(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "preview-owner@example.test")
	var list domain.WishlistResponse
	created := f.send(t, "POST", "/api/v1/wishlists", `{"name":"<script> & gifts","occasion":"other","message":"Hidden message"}`, nil)
	version := created.Header.Get("ETag")
	requireStatus(t, created, 201, &list)
	var link domain.WishlistShareLinkResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil), 201, &link)
	u, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	visitor := f.visitor(t)
	path := "/api/v1/shared-wishlists/" + link.Id + "/preview"
	read := func(method, path string) ([]byte, int, string) {
		t.Helper()
		r := visitor.send(t, method, path, "", nil)
		content, err := io.ReadAll(r.Body)
		r.Body.Close()
		if err != nil {
			t.Fatal(err)
		}
		if r.Header.Get("Cache-Control") != "no-store" {
			t.Fatal("public preview must not be cached")
		}
		return content, r.StatusCode, r.Header.Get("Content-Length")
	}
	// Act
	content, status, length := read("GET", path)
	// Assert
	if status != 200 || !strings.Contains(string(content), "&lt;script&gt; &amp; gifts") || strings.Contains(string(content), "<script>") || strings.Contains(string(content), "Hidden message") || strings.Contains(string(content), u.Fragment) || strings.Contains(string(content), "og:image") || length != strconv.Itoa(len(content)) {
		t.Fatal("public metadata escaped incorrectly or disclosed private fields")
	}
	head, status, headLength := read("HEAD", path)
	if status != 200 || len(head) != 0 || headLength != length {
		t.Fatal("HTML HEAD differs from GET")
	}
	requireStatus(t, visitor.send(t, "GET", path+"/image", "", nil), 404, nil)
	for index := 0; index < 3; index++ {
		var wish domain.WishResponse
		created = f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Private wish text"}`, nil)
		wishVersion := created.Header.Get("ETag")
		requireStatus(t, created, 201, &wish)
		requireStatus(t, f.upload(t, "/api/v1/wishlists/"+list.Id+"/wishes/"+wish.Id+"/image", wishVersion, uint8(index+42)), 200, nil)
	}
	content, status, _ = read("GET", path)
	if status != 200 || !strings.Contains(string(content), "og:image") || strings.Contains(string(content), "Private wish text") {
		t.Fatal("preview graph differs")
	}
	content, status, length = read("GET", path+"/image")
	cfg, format, err := image.DecodeConfig(bytes.NewReader(content))
	if status != 200 || err != nil || format != "jpeg" || cfg.Width != 1200 || cfg.Height != 630 {
		t.Fatal("preview image contract differs")
	}
	head, status, headLength = read("HEAD", path+"/image")
	if status != 200 || len(head) != 0 || length != headLength {
		t.Fatal("JPEG HEAD differs from GET")
	}
	requireStatus(t, f.send(t, "PATCH", "/api/v1/wishlists/"+list.Id, `{"isArchived":true}`, map[string]string{"If-Match": version}), 200, nil)
	for _, method := range []string{"GET", "HEAD"} {
		requireStatus(t, visitor.send(t, method, path, "", nil), 404, nil)
		requireStatus(t, visitor.send(t, method, path+"/image", "", nil), 404, nil)
	}
}
