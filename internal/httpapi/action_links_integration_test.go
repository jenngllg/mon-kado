//go:build integration

package httpapi_test

import (
	"net/url"
	"testing"
)

func actionCredentials(t *testing.T, link *url.URL) url.Values {
	t.Helper()
	if link.RawQuery != "" || link.ForceQuery || link.Fragment == "" {
		t.Fatal("authentication credentials must remain in a frontend-only fragment")
	}
	credentials, err := url.ParseQuery(link.Fragment)
	if err != nil || len(credentials["token"]) != 1 || credentials.Get("token") == "" {
		t.Fatal("authentication fragment must contain exactly one token")
	}
	return credentials
}
