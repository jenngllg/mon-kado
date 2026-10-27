//go:build integration

package httpapi_test

import (
	"bytes"
	"encoding/json"
	"image"
	_ "image/png"
	"io"
	"net/url"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestGroupJoinPreview_WhenAdvertised_ExposesIdentityWithoutEnrolling(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	visitor := s.owner.visitor(t)
	previewPath := s.base + "/preview"
	requireStatus(t, visitor.send(t, "GET", previewPath, "", nil), 404, nil)
	var link domain.GroupJoinLinkResponse
	created := s.owner.send(t, "POST", s.base+"/join-link", "", nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &link)
	// Act
	response := visitor.send(t, "GET", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": link.Code})
	var fields map[string]json.RawMessage
	requireStatus(t, response, 200, &fields)
	// Assert
	if len(fields) != 4 || string(fields["id"]) != `"`+s.group.Id+`"` || string(fields["memberCount"]) != "1" || string(fields["profileImageUrl"]) != "null" || fields["name"] == nil {
		t.Fatal("preview must expose exactly the advertised identity")
	}
	requireStatus(t, s.foreign.send(t, "GET", s.base, "", nil), 404, nil)
	var group domain.GroupResponse
	requireStatus(t, s.owner.send(t, "GET", s.base, "", nil), 200, &group)
	if group.MemberCount != 1 {
		t.Fatal("preview enrolled a visitor")
	}
	for _, test := range []struct {
		code   string
		status int
	}{{"", 400}, {"invalid", 400}, {strings.Repeat("A", 16), 404}} {
		requireStatus(t, visitor.send(t, "GET", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": test.code}), test.status, nil)
	}
	var preview domain.GroupJoinPreviewResponse
	requireStatus(t, visitor.send(t, "GET", previewPath, "", map[string]string{"Accept": "application/json"}), 200, &preview)
	if preview.Id != group.Id || preview.Name != group.Name {
		t.Fatal("public preview differs from code preview")
	}
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/join-link", "", map[string]string{"If-Match": etag}), 204, nil)
	for _, path := range []string{previewPath, previewPath + "/image", "/api/v1/groups/join"} {
		requireStatus(t, visitor.send(t, "GET", path, "", map[string]string{"X-MonKado-Share-Token": link.Code}), 404, nil)
	}
}

func TestGroupJoinPreview_WhenCrawlerReads_EmitsEscapedMetadataWithoutAdmissionCode(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	var group domain.GroupResponse
	requireStatus(t, s.owner.send(t, "PUT", s.base, `{"name":"Famille <script> & amis"}`, map[string]string{"If-Match": s.etag}), 200, &group)
	var link domain.GroupJoinLinkResponse
	requireStatus(t, s.owner.send(t, "POST", s.base+"/join-link", "", nil), 201, &link)
	visitor := s.owner.visitor(t)
	// Act
	response := visitor.send(t, "GET", s.base+"/preview", "", nil)
	content, err := io.ReadAll(response.Body)
	response.Body.Close()
	// Assert
	if err != nil || response.StatusCode != 200 || response.Header.Get("Content-Type") != "text/html; charset=utf-8" || response.Header.Get("Cache-Control") != "no-store" {
		t.Fatal("invalid crawler response")
	}
	html := string(content)
	for _, required := range []string{`property="og:title"`, `property="og:image"`, `name="twitter:card"`, "Famille &lt;script&gt; &amp; amis", "/groups/join/" + s.group.Id, "/group-previews/" + s.group.Id + "/image"} {
		if !strings.Contains(html, required) {
			t.Fatalf("missing metadata %s", required)
		}
	}
	for _, secret := range []string{link.Code, "<script>", s.ownerID, "protected_code", "code_hash"} {
		if strings.Contains(html, secret) {
			t.Fatal("private or executable content leaked into social metadata")
		}
	}
	imageResponse := visitor.send(t, "GET", s.base+"/preview/image", "", nil)
	content, err = io.ReadAll(imageResponse.Body)
	imageResponse.Body.Close()
	dimensions, format, decodeError := image.DecodeConfig(bytes.NewReader(content))
	if err != nil || decodeError != nil || imageResponse.StatusCode != 200 || format != "png" || dimensions.Width != 1200 || dimensions.Height != 630 {
		t.Fatal("missing proportional branded fallback")
	}
}

func TestGroupJoinPreview_WhenPhotoChanges_UsesCurrentPhotoAndRevokesOnLinkDeletion(t *testing.T) {
	// Arrange
	s := newGroupScenario(t)
	var group domain.GroupResponse
	upload := s.owner.upload(t, s.base+"/image", s.etag, 42, map[string]string{"X-CSRF-TOKEN": s.owner.csrf})
	groupETag := upload.Header.Get("ETag")
	requireStatus(t, upload, 200, &group)
	var link domain.GroupJoinLinkResponse
	created := s.owner.send(t, "POST", s.base+"/join-link", "", nil)
	etag := created.Header.Get("ETag")
	requireStatus(t, created, 201, &link)
	visitor := s.owner.visitor(t)
	var preview domain.GroupJoinPreviewResponse
	requireStatus(t, visitor.send(t, "GET", "/api/v1/groups/join", "", map[string]string{"X-MonKado-Share-Token": link.Code}), 200, &preview)
	if preview.ProfileImageUrl == nil {
		t.Fatal("missing advertised photo")
	}
	photo, err := url.Parse(*preview.ProfileImageUrl)
	if err != nil || photo.Path != s.base+"/preview/image" || photo.RawQuery != "" {
		t.Fatal("preview image must not contain an admission secret")
	}
	// Act
	response := visitor.send(t, "GET", photo.RequestURI(), "", nil)
	content, err := io.ReadAll(response.Body)
	response.Body.Close()
	// Assert
	if err != nil || response.StatusCode != 200 || response.Header.Get("Content-Type") != "image/webp" || response.Header.Get("Cache-Control") != "no-store" || len(content) == 0 {
		t.Fatal("advertised photo unavailable")
	}
	requireStatus(t, s.owner.upload(t, s.base+"/image", groupETag, 99, map[string]string{"X-CSRF-TOKEN": s.owner.csrf}), 200, &group)
	replacement := visitor.send(t, "GET", photo.RequestURI(), "", nil)
	replacementContent, err := io.ReadAll(replacement.Body)
	replacement.Body.Close()
	if err != nil || replacement.StatusCode != 200 || bytes.Equal(content, replacementContent) {
		t.Fatal("preview did not use the current group photo")
	}
	requireStatus(t, s.owner.send(t, "DELETE", s.base+"/join-link", "", map[string]string{"If-Match": etag}), 204, nil)
	requireStatus(t, visitor.send(t, "GET", photo.RequestURI(), "", nil), 404, nil)
}
