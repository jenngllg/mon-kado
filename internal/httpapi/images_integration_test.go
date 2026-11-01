//go:build integration

package httpapi_test

import (
	"bytes"
	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/worker"
	"image"
	"image/color"
	"image/png"
	"io"
	"log/slog"
	"mime/multipart"
	"net/http"
	"net/netip"
	"net/url"
	"os"
	"path/filepath"
	"testing"
	"time"
)

func (f *fixture) upload(t *testing.T, path, etag string, seed uint8, headers ...map[string]string) *http.Response {
	t.Helper()
	var source bytes.Buffer
	picture := image.NewRGBA(image.Rect(0, 0, 4, 3))
	picture.Set(1, 1, color.RGBA{R: seed, A: 255})
	if err := png.Encode(&source, picture); err != nil {
		t.Fatal(err)
	}
	var body bytes.Buffer
	writer := multipart.NewWriter(&body)
	part, err := writer.CreateFormFile("image", "gift.png")
	if err != nil {
		t.Fatal(err)
	}
	if _, err = part.Write(source.Bytes()); err != nil {
		t.Fatal(err)
	}
	if err = writer.Close(); err != nil {
		t.Fatal(err)
	}
	r, err := http.NewRequestWithContext(t.Context(), "PUT", f.server.URL+path, &body)
	if err != nil {
		t.Fatal(err)
	}
	r.Header.Set("Content-Type", writer.FormDataContentType())
	r.Header.Set("Authorization", "Bearer "+f.access)
	r.Header.Set("If-Match", etag)
	for _, values := range headers {
		for key, value := range values {
			r.Header.Set(key, value)
		}
	}
	response, err := f.client.Do(r)
	if err != nil {
		t.Fatal(err)
	}
	return response
}

func TestWishImages_WhenUploadedBehindTrustedTLSProxy_ReturnsReadableHTTPSURL(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.service.Config.TrustedProxies = []netip.Prefix{netip.MustParsePrefix("127.0.0.1/32")}
	f.member(t, "proxy-image@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Pictures","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	path := "/api/v1/wishlists/" + list.Id + "/wishes"
	created := f.send(t, "POST", path, `{"name":"Gift"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	path += "/" + wish.Id
	headers := map[string]string{"X-Forwarded-Proto": "https"}
	// Act
	uploaded := f.upload(t, path+"/image", etag, 42, headers)
	nextVersion := uploaded.Header.Get("ETag")
	requireStatus(t, uploaded, 200, &wish)
	// Assert
	if wish.ImageUrl == nil || nextVersion == etag || nextVersion == "" {
		t.Fatal("uploaded image or updated version missing")
	}
	imageURL, err := url.Parse(*wish.ImageUrl)
	serverURL, _ := url.Parse(f.server.URL)
	if err != nil || imageURL.Scheme != "https" || imageURL.Host != serverURL.Host || imageURL.Path != path+"/image" || imageURL.Query().Get("token") == "" {
		t.Fatal("upload URL does not preserve the public HTTPS origin and image grant")
	}
	requireStatus(t, f.send(t, "GET", imageURL.RequestURI(), "", nil), 200, nil)
	var reread domain.WishResponse
	requireStatus(t, f.send(t, "GET", path, "", headers), 200, &reread)
	if reread.ImageUrl == nil {
		t.Fatal("saved image missing on subsequent read")
	}
	readURL, err := url.Parse(*reread.ImageUrl)
	if err != nil || readURL.Scheme != "https" || readURL.Host != serverURL.Host {
		t.Fatal("subsequent read lost its HTTPS origin")
	}
}

func TestProfileImages_WhenReplacedAndRemoved_InvalidatesPublicURLsAndCleansFiles(t *testing.T) {
	// Arrange
	f := newFixture(t)
	member := f.member(t, "photo@example.test")
	profile := f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil)
	etag := profile.Header.Get("ETag")
	requireStatus(t, profile, 200, nil)
	path := "/api/v1/members/current/profile/image"
	var result domain.MemberProfileResponse
	// Act
	uploaded := f.upload(t, path, etag, 42)
	next := uploaded.Header.Get("ETag")
	requireStatus(t, uploaded, 200, &result)
	if result.ProfileImageUrl == nil {
		t.Fatal("public photo URL missing")
	}
	old, err := url.Parse(*result.ProfileImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	ownerAccess := f.access
	f.access = ""
	photo := f.send(t, "GET", old.RequestURI(), "", nil)
	content, err := io.ReadAll(photo.Body)
	photo.Body.Close()
	if err != nil || photo.StatusCode != 200 || photo.Header.Get("Content-Type") != "image/webp" {
		t.Fatal("public photo is not readable")
	}
	decoded, _, err := image.DecodeConfig(bytes.NewReader(content))
	if err != nil || decoded.Width != 4 || decoded.Height != 3 {
		t.Fatal("profile photo dimensions or aspect ratio changed")
	}
	requireStatus(t, f.send(t, "GET", "/api/v1/members/"+member+"/profile/image?imageId=invalid", "", nil), 404, nil)
	f.access = ownerAccess
	requireStatus(t, f.upload(t, path, etag, 99), 412, nil)
	replaced := f.upload(t, path, next, 99)
	next = replaced.Header.Get("ETag")
	requireStatus(t, replaced, 200, &result)
	current, err := url.Parse(*result.ProfileImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	requireStatus(t, f.send(t, "GET", old.RequestURI(), "", nil), 404, nil)
	deleted := f.send(t, "DELETE", path, "", map[string]string{"If-Match": next})
	next = deleted.Header.Get("ETag")
	requireStatus(t, deleted, 204, nil)
	requireStatus(t, f.send(t, "GET", current.RequestURI(), "", nil), 404, nil)
	requireStatus(t, f.send(t, "DELETE", path, "", map[string]string{"If-Match": next}), 404, nil)
	w := worker.Worker{Service: f.service, Logger: slog.New(slog.NewJSONHandler(io.Discard, nil))}
	t.Cleanup(w.Close)
	if err := w.Tick(t.Context()); err != nil {
		t.Fatal(err)
	}
	// Assert
	requireStatus(t, f.send(t, "GET", "/api/v1/auth/sessions/current", "", nil), 200, &result)
	if result.ProfileImageUrl != nil {
		t.Fatal("removed photo is still referenced")
	}
	entries, err := os.ReadDir(f.service.Config.ImagePath)
	if err != nil || len(entries) != 0 {
		t.Fatalf("obsolete or failed image uploads were retained: %v", err)
	}
}

func TestSharedImages_WhenShareLinkRevoked_InvalidatesResourceBoundGrant(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "sharedphoto@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Pictures","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	created := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Gift"}`, nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &wish)
	requireStatus(t, f.upload(t, "/api/v1/wishlists/"+list.Id+"/wishes/"+wish.Id+"/image", etag, 42), 200, &wish)
	var link domain.WishlistShareLinkResponse
	created = f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/share-link", "", nil)
	etag = created.Header.Get("ETag")
	requireStatus(t, created, 201, &link)
	parsed, err := url.Parse(link.ShareUrl)
	if err != nil {
		t.Fatal(err)
	}
	headers := map[string]string{"X-MonKado-Share-Token": parsed.Fragment}
	visitor := f.visitor(t)
	base := "/api/v1/shared-wishlists/" + link.Id
	requireStatus(t, visitor.send(t, "GET", base+"/participants/current", "", headers), 401, nil)
	requireStatus(t, visitor.send(t, "POST", base+"/participants", `{"displayName":"Visitor"}`, headers), 201, nil)
	var participant domain.WishlistParticipantResponse
	requireStatus(t, visitor.send(t, "GET", base+"/participants/current", "", headers), 200, &participant)
	var shared domain.SharedWishDetailResponse
	requireStatus(t, visitor.send(t, "GET", base+"/wishes/"+wish.Id, "", headers), 200, &shared)
	if shared.ImageUrl == nil || participant.Id == "" {
		t.Fatal("shared response lacks its public contracts")
	}
	grant, err := url.Parse(*shared.ImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	// Act
	requireStatus(t, visitor.send(t, "GET", grant.RequestURI(), "", nil), 200, nil)
	requireStatus(t, f.send(t, "DELETE", "/api/v1/wishlists/"+list.Id+"/share-link", "", map[string]string{"If-Match": etag}), 204, nil)
	// Assert
	requireStatus(t, visitor.send(t, "GET", grant.RequestURI(), "", nil), 404, nil)
	requireStatus(t, visitor.send(t, "GET", base+"/wishes/"+wish.Id, "", headers), 404, nil)
	files, err := filepath.Glob(filepath.Join(f.service.Config.ImagePath, "*.webp"))
	if err != nil || len(files) != 1 {
		t.Fatal("revoking sharing deleted the owner's image")
	}
}

func TestImages_WhenReplacedAndGrantExpired_PreventsAccessToOldPrivateImage(t *testing.T) {
	// Arrange
	f := newFixture(t)
	f.member(t, "owner@example.test")
	var list domain.WishlistResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/wishlists", `{"name":"Pictures","occasion":"other"}`, nil), 201, &list)
	var wish domain.WishResponse
	r := f.send(t, "POST", "/api/v1/wishlists/"+list.Id+"/wishes", `{"name":"Gift"}`, nil)
	etag := r.Header.Get("ETag")
	requireStatus(t, r, 201, &wish)
	path := "/api/v1/wishlists/" + list.Id + "/wishes/" + wish.Id + "/image"
	// Act
	r = f.upload(t, path, etag, 42)
	next := r.Header.Get("ETag")
	requireStatus(t, r, 200, &wish)
	if wish.ImageUrl == nil {
		t.Fatal("image URL missing")
	}
	u, err := url.Parse(*wish.ImageUrl)
	if err != nil {
		t.Fatal(err)
	}
	oldURL := u.RequestURI()
	f.access = ""
	r = f.send(t, "GET", oldURL, "", nil)
	data, _ := io.ReadAll(r.Body)
	r.Body.Close()
	if r.StatusCode != 200 || r.Header.Get("Content-Type") != "image/webp" || len(data) < 12 || string(data[0:4]) != "RIFF" {
		t.Fatal("normalized private image cannot be read through its grant")
	}
	requireStatus(t, f.send(t, "GET", path, "", nil), 404, nil)
	f.access = ""
	f.csrfToken(t)
	var login domain.AccessTokenResponse
	requireStatus(t, f.send(t, "POST", "/api/v1/auth/sessions", `{"email":"owner@example.test","password":"A long test password 🎁"}`, nil), 200, &login)
	f.access = login.AccessToken
	r = f.upload(t, path, next, 99)
	next = r.Header.Get("ETag")
	requireStatus(t, r, 200, &wish)
	// Assert
	requireStatus(t, f.send(t, "GET", oldURL, "", nil), 404, nil)
	u, _ = url.Parse(*wish.ImageUrl)
	now := f.service.Now().Add(6 * time.Minute)
	f.service.Now = func() time.Time { return now }
	requireStatus(t, f.send(t, "GET", u.RequestURI(), "", nil), 404, nil)
	deleted := f.send(t, "DELETE", path, "", map[string]string{"If-Match": next})
	next = deleted.Header.Get("ETag")
	requireStatus(t, deleted, 204, nil)
	requireStatus(t, f.send(t, "DELETE", path, "", map[string]string{"If-Match": next}), 404, nil)
}
