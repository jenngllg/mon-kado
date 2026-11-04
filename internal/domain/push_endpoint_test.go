package domain_test

import (
	"github.com/JennGllg/mon-kado/internal/domain"
	"testing"
)

func TestPushEndpoint_WhenUntrusted_RejectsCapabilityAndSSRFBypasses(t *testing.T) {
	for _, tc := range []struct {
		url   string
		valid bool
	}{{"https://fcm.googleapis.com/fcm/send/device", true}, {"https://web.push.apple.com/device", true}, {"https://updates.push.services.mozilla.com/wpush/v2/device", true}, {"http://fcm.googleapis.com/device", false}, {"https://fcm.googleapis.com:443/device", false}, {"https://fcm.googleapis.com.evil.test/device", false}, {"https://fcm.googleapis.com@localhost/device", false}, {"https://127.0.0.1/device", false}, {"https://fcm.googleapis.com/device?secret=bad", false}, {"https://fcm.googleapis.com/device#bad", false}, {"https://fcm.googleapis.com/", false}, {"https://fcm.googleapis.com/device\n", false}, {"%", false}} {
		t.Run(tc.url, func(t *testing.T) {
			if domain.ValidPushEndpoint(tc.url) != tc.valid {
				t.Fatal("incorrect endpoint classification")
			}
		})
	}
}
