package domain

import (
	"net/url"
	"strings"
)

// ValidPushEndpoint confines outgoing requests to known browser push services.
func ValidPushEndpoint(value string) bool {
	u, e := url.Parse(value)
	if e != nil || len(value) > 2048 || strings.ContainsAny(value, " \t\r\n") || u.Scheme != "https" || u.User != nil || u.Fragment != "" || u.RawQuery != "" || u.Port() != "" || u.Path == "" || u.Path == "/" {
		return false
	}
	switch u.Host {
	case "fcm.googleapis.com", "updates.push.services.mozilla.com", "web.push.apple.com":
		return true
	}
	return false
}
