package httpapi

import (
	"net/http/httptest"
	"net/netip"
	"testing"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/domain"
)

func TestAbsoluteImageURLs_WhenTransportDiffers_UsesOnlyTrustedHTTPS(t *testing.T) {
	for _, test := range []struct {
		name, target, peer, forwarded, scheme string
		production                            bool
	}{
		{"directHTTP", "http://localhost:7001", "192.0.2.1:1234", "", "http", false},
		{"directTLS", "https://localhost:7001", "192.0.2.1:1234", "http", "https", false},
		{"trustedHTTPS", "http://localhost:7001", "127.0.0.1:1234", "https", "https", false},
		{"trustedIPv6HTTPS", "http://localhost:7001", "[::1]:1234", "https", "https", false},
		{"mappedIPv4HTTPS", "http://localhost:7001", "[::ffff:127.0.0.1]:1234", "https", "https", false},
		{"untrustedSpoof", "http://localhost:7001", "192.0.2.1:1234", "https", "http", false},
		{"invalidPeer", "http://localhost:7001", "invalid", "https", "http", false},
		{"multipleProtocols", "http://localhost:7001", "127.0.0.1:1234", "https,http", "http", false},
		{"invalidProtocol", "http://localhost:7001", "127.0.0.1:1234", "javascript", "http", false},
		{"trustedHTTP", "http://localhost:7001", "127.0.0.1:1234", "http", "http", false},
		{"production", "http://localhost:7001", "127.0.0.1:1234", "http", "https", true},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			a := &API{Service: &application.Service{Config: config.Config{
				Production:     test.production,
				TrustedProxies: []netip.Prefix{netip.MustParsePrefix("127.0.0.1/32"), netip.MustParsePrefix("::1/128")},
			}}}
			r := httptest.NewRequest("GET", test.target+"/api/v1/wishlists", nil)
			r.RemoteAddr = test.peer
			r.Header.Set("X-Forwarded-Proto", test.forwarded)
			r.Header.Set("X-Forwarded-Host", "attacker.test")
			wishURL, profileURL := "/api/v1/wishlists/list/wishes/wish/image?token=test", "/api/v1/members/member/profile/image?imageId=test"
			body := struct {
				Wishes  []domain.WishResponse
				Session *domain.CurrentSessionResponse
			}{[]domain.WishResponse{{ImageUrl: &wishURL}}, &domain.CurrentSessionResponse{ProfileImageUrl: &profileURL}}
			// Act
			a.absoluteImageURLs(r, &body)
			// Assert
			origin := test.scheme + "://localhost:7001"
			if wishURL != origin+"/api/v1/wishlists/list/wishes/wish/image?token=test" || profileURL != origin+"/api/v1/members/member/profile/image?imageId=test" {
				t.Fatal("image origin or resource-bound query changed")
			}
		})
	}
}

func TestAbsoluteImageURLs_WhenURLsAreAbsentOrAbsolute_PreservesValues(t *testing.T) {
	// Arrange
	a := &API{Service: &application.Service{}}
	r := httptest.NewRequest("GET", "https://localhost:7001", nil)
	existing := "https://images.example.test/image.webp"
	body := []domain.WishResponse{{ImageUrl: nil}, {ImageUrl: &existing}}
	// Act
	a.absoluteImageURLs(r, body)
	a.absoluteImageURLs(r, nil)
	// Assert
	if body[0].ImageUrl != nil || existing != "https://images.example.test/image.webp" {
		t.Fatal("missing or absolute image URL changed")
	}
}
