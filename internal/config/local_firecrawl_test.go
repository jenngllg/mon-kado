package config

import (
	"strings"
	"testing"
	"time"
)

func TestLoad_WhenPreviewBudgetConfigured_ReservesTimeWithinTenSecondUI(t *testing.T) {
	for _, tc := range []struct {
		name, timeout  string
		enabled, valid bool
	}{
		{"ordinary maximum", "8s", false, true},
		{"ordinary extension rejected", "8.1s", false, false},
		{"local maximum", "8s", true, true},
		{"local excess rejected", "8.1s", true, false},
		{"former local extension rejected", "35s", true, false},
		{"default", "", false, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("WISH_IMPORT_TIMEOUT", tc.timeout)
			if tc.enabled {
				t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", "true")
				t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY", strings.Repeat("a", 64))
				t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED", "true")
				t.Setenv("FIRECRAWL_API_KEY", "fc-"+strings.Repeat("a", 32))
			}
			// Act
			result, err := Load()
			// Assert
			if (err == nil) != tc.valid {
				t.Fatalf("unexpected validity: %v", err)
			}
			if tc.valid {
				wanted, _ := time.ParseDuration(tc.timeout)
				if tc.timeout == "" {
					wanted = MaximumImportDuration
				}
				if result.Imports().Timeout != wanted {
					t.Fatal("configured budget was not retained")
				}
			}
		})
	}
}

func TestLoad_WhenFirecrawlConfigurationInvalid_RejectsBeforeStarting(t *testing.T) {
	for _, scenario := range []struct{ name, enabled, key, environment, cloudflare string }{
		{"toggle", "bad", "", "local", "true"}, {"key missing", "true", "", "local", "true"}, {"key invalid", "true", "bad", "local", "true"}, {"no primary", "true", "fc-" + strings.Repeat("a", 32), "local", "false"}, {"production", "true", "fc-" + strings.Repeat("a", 32), "production", "true"},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("APP_ENV", scenario.environment)
			t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", scenario.cloudflare)
			t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY", strings.Repeat("a", 64))
			t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED", scenario.enabled)
			t.Setenv("FIRECRAWL_API_KEY", scenario.key)
			// Act / Assert
			if _, err := Load(); err == nil {
				t.Fatal("invalid local fallback configuration accepted")
			}
		})
	}
}

func TestLoad_WhenLocalFirecrawlEnabled_PreservesPrivateKeyAndDefaultOff(t *testing.T) {
	// Arrange
	validEnvironment(t)
	// Act / Assert
	disabled, err := Load()
	if err != nil || disabled.Imports().LocalFirecrawlEnabled || disabled.Imports().LocalFirecrawlKey != "" {
		t.Fatal("fallback enabled by default")
	}
	// Arrange
	key := "fc-" + strings.Repeat("a", 32)
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", "true")
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY", strings.Repeat("a", 64))
	t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED", "true")
	t.Setenv("FIRECRAWL_API_KEY", key)
	// Act
	enabled, err := Load()
	defaults := Config{ImportOptions: ImportOptions{LocalFirecrawlEnabled: true, LocalFirecrawlKey: key}}.Imports()
	// Assert
	if err != nil || !enabled.Imports().LocalFirecrawlEnabled || enabled.Imports().LocalFirecrawlKey != key || !defaults.LocalFirecrawlEnabled || defaults.LocalFirecrawlKey != key {
		t.Fatal("local fallback configuration lost")
	}
}
