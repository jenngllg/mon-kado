package httpapi

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http/httptest"
	"net/netip"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestAbsoluteImageURLs_WhenTransportDiffers_UsesOnlyTrustedHTTPS(t *testing.T) {
	for _, test := range []struct {
		name, target, peer, forwarded, secondProtocol, scheme string
		production                                            bool
	}{
		{"directHTTP", "http://localhost:7001", "192.0.2.1:1234", "", "", "http", false},
		{"directTLS", "https://localhost:7001", "192.0.2.1:1234", "http", "", "https", false},
		{"trustedHTTPS", "http://localhost:7001", "127.0.0.1:1234", "https", "", "https", false},
		{"trustedIPv6HTTPS", "http://localhost:7001", "[::1]:1234", "https", "", "https", false},
		{"mappedIPv4HTTPS", "http://localhost:7001", "[::ffff:127.0.0.1]:1234", "https", "", "https", false},
		{"untrustedSpoof", "http://localhost:7001", "192.0.2.1:1234", "https", "", "http", false},
		{"invalidPeer", "http://localhost:7001", "invalid", "https", "", "http", false},
		{"multipleProtocols", "http://localhost:7001", "127.0.0.1:1234", "https,http", "", "http", false},
		{"duplicateHeaders", "http://localhost:7001", "127.0.0.1:1234", "https", "http", "http", false},
		{"invalidProtocol", "http://localhost:7001", "127.0.0.1:1234", "javascript", "", "http", false},
		{"trustedHTTP", "http://localhost:7001", "127.0.0.1:1234", "http", "", "http", false},
		{"production", "http://localhost:7001", "127.0.0.1:1234", "http", "", "https", true},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			a := New(&application.Service{Now: time.Now, Config: config.Config{
				Production:     test.production,
				AllowedHosts:   []string{"localhost"},
				TrustedProxies: []netip.Prefix{netip.MustParsePrefix("127.0.0.1/32"), netip.MustParsePrefix("::1/128")},
			}}, slog.New(slog.NewJSONHandler(io.Discard, nil)))
			r := httptest.NewRequest("GET", test.target+"/image-url-test", nil)
			r.RemoteAddr = test.peer
			r.Header.Set("X-Forwarded-Proto", test.forwarded)
			if test.secondProtocol != "" {
				r.Header.Add("X-Forwarded-Proto", test.secondProtocol)
			}
			r.Header.Set("X-Forwarded-Host", "attacker.test")
			wishURL, profileURL := "/api/v1/wishlists/list/wishes/wish/image?token=test", "/api/v1/members/member/profile/image?imageId=test"
			body := struct {
				Wishes  []domain.WishResponse
				Session *domain.CurrentSessionResponse
			}{[]domain.WishResponse{{ImageUrl: &wishURL}}, &domain.CurrentSessionResponse{ProfileImageUrl: &profileURL}}
			a.route("GET /image-url-test", options{}, func(*request) (response, error) { return response{Body: &body}, nil })
			w := httptest.NewRecorder()
			// Act
			a.ServeHTTP(w, r)
			// Assert
			if w.Code != 200 || json.Unmarshal(w.Body.Bytes(), &body) != nil {
				t.Fatal("image response did not serialize successfully")
			}
			origin := test.scheme + "://localhost:7001"
			if *body.Wishes[0].ImageUrl != origin+"/api/v1/wishlists/list/wishes/wish/image?token=test" || *body.Session.ProfileImageUrl != origin+"/api/v1/members/member/profile/image?imageId=test" {
				t.Fatal("image origin or resource-bound query changed")
			}
		})
	}
}

func TestAbsoluteImageURLs_WhenURLsAreAbsentOrAbsolute_PreservesValues(t *testing.T) {
	// Arrange
	a := New(&application.Service{Now: time.Now, Config: config.Config{AllowedHosts: []string{"localhost"}}}, slog.New(slog.NewJSONHandler(io.Discard, nil)))
	r := httptest.NewRequest("GET", "https://localhost:7001/image-url-test", nil)
	existing := "https://images.example.test/image.webp"
	body := []domain.WishResponse{{ImageUrl: nil}, {ImageUrl: &existing}}
	a.route("GET /image-url-test", options{}, func(*request) (response, error) { return response{Body: body}, nil })
	w := httptest.NewRecorder()
	// Act
	a.ServeHTTP(w, r)
	// Assert
	if w.Code != 200 || json.Unmarshal(w.Body.Bytes(), &body) != nil || body[0].ImageUrl != nil || *body[1].ImageUrl != "https://images.example.test/image.webp" {
		t.Fatal("missing or absolute image URL changed")
	}
}
