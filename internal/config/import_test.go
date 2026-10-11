package config

import (
	"testing"
	"time"
)

func TestLoad_WhenImportBudgetInvalid_RejectsUnsupportedSecurityLimits(t *testing.T) {
	for _, test := range []struct{ key, value string }{
		{"WISH_IMPORT_TIMEOUT", "invalid"}, {"WISH_IMPORT_TIMEOUT", "500ms"}, {"WISH_IMPORT_TIMEOUT", "21s"},
		{"WISH_IMPORT_MAXIMUM_HTML_BYTES", "bad"}, {"WISH_IMPORT_MAXIMUM_HTML_BYTES", "0"}, {"WISH_IMPORT_MAXIMUM_HTML_BYTES", "2097153"},
		{"WISH_IMPORT_MAXIMUM_REDIRECTS", "-1"}, {"WISH_IMPORT_MAXIMUM_REDIRECTS", "4"},
		{"WISH_IMPORT_PERMIT_LIMIT", "0"}, {"WISH_IMPORT_PERMIT_LIMIT", "11"},
	} {
		t.Run(test.key+"="+test.value, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv(test.key, test.value)
			// Act
			_, err := Load()
			// Assert
			if err == nil {
				t.Fatal("unsupported import budget accepted")
			}
		})
	}
}

func TestLoad_WhenImportBudgetsReduced_PreservesZeroRedirectsAndPerMemberQuota(t *testing.T) {
	// Arrange
	validEnvironment(t)
	t.Setenv("WISH_IMPORT_TIMEOUT", "1s")
	t.Setenv("WISH_IMPORT_MAXIMUM_HTML_BYTES", "1024")
	t.Setenv("WISH_IMPORT_MAXIMUM_REDIRECTS", "0")
	t.Setenv("WISH_IMPORT_PERMIT_LIMIT", "1")
	// Act
	configuration, err := Load()
	options := configuration.Imports()
	// Assert
	if err != nil || options.Timeout != time.Second || options.MaximumHTMLBytes != 1024 || options.MaximumRedirects != 0 || options.PermitLimit != 1 {
		t.Fatalf("reduced import budgets changed: %#v %v", options, err)
	}
}
