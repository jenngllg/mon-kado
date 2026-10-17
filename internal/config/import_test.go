package config

import (
	"testing"
	"time"
)

func TestLoad_WhenImportBudgetInvalid_RejectsUnsupportedSecurityLimits(t *testing.T) {
	for _, test := range []struct{ key, value string }{
		{"WISH_IMPORT_TIMEOUT", "invalid"}, {"WISH_IMPORT_TIMEOUT", "500ms"}, {"WISH_IMPORT_TIMEOUT", "21s"},
		{"WISH_IMPORT_MAXIMUM_HTML_BYTES", "bad"}, {"WISH_IMPORT_MAXIMUM_HTML_BYTES", "0"}, {"WISH_IMPORT_MAXIMUM_HTML_BYTES", "4194305"},
		{"WISH_IMPORT_MAXIMUM_REDIRECTS", "-1"}, {"WISH_IMPORT_MAXIMUM_REDIRECTS", "4"},
		{"WISH_IMPORT_PERMIT_LIMIT", "0"}, {"WISH_IMPORT_PERMIT_LIMIT", "11"},
		{"WISH_IMPORT_CATALOG_ENABLED", "invalid"},
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

func TestLoad_WhenPublicCatalogIsEnabled_RequiresBoundedPublicIndexIdentifier(t *testing.T) {
	for _, test := range []struct {
		key   string
		valid bool
	}{{"key_public", true}, {"", false}, {"key_", false}, {"admin_secret", false}, {"key_bad-characters", false}} {
		t.Run(test.key, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("WISH_IMPORT_CATALOG_ENABLED", "true")
			t.Setenv("WISH_IMPORT_CATALOG_INDEX_KEY", test.key)
			// Act
			configuration, err := Load()
			// Assert
			if (err == nil) != test.valid {
				t.Fatal("public catalog configuration was accepted incorrectly")
			}
			if test.valid && (!configuration.Imports().CatalogEnabled || configuration.Imports().CatalogIndexKey != test.key) {
				t.Fatal("catalog configuration lost")
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

func TestLoad_WhenMerchantPageBudgetUsesSupportedBoundary_PreservesExplicitOverrides(t *testing.T) {
	for _, test := range []struct {
		name, value string
		want        int
	}{
		{"default", "", MaximumImportHTMLBytes},
		{"maximum", "4194304", MaximumImportHTMLBytes},
		{"explicit former budget", "2097152", 2 * 1024 * 1024},
	} {
		t.Run(test.name, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("WISH_IMPORT_MAXIMUM_HTML_BYTES", test.value)
			// Act
			configuration, err := Load()
			// Assert
			if err != nil || configuration.Imports().MaximumHTMLBytes != test.want {
				t.Fatal("merchant page budget was not preserved")
			}
		})
	}
}
