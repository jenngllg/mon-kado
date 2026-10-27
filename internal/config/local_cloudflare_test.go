package config

import (
	"strings"
	"testing"
)

func TestLoad_WhenLocalCloudflareConfigurationInvalid_RejectsBeforeStarting(t *testing.T) {
	for _, scenario := range []struct{ name, enabled, key, environment string }{
		{"invalid toggle", "invalid", "", "local"},
		{"missing key", "true", "", "local"},
		{"short key", "true", "ab", "local"},
		{"nonhex key", "true", strings.Repeat("z", 64), "local"},
		{"production", "true", strings.Repeat("a", 64), "production"},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			validEnvironment(t)
			t.Setenv("APP_ENV", scenario.environment)
			t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", scenario.enabled)
			t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY", scenario.key)
			// Act
			_, err := Load()
			// Assert
			if err == nil || !strings.Contains(strings.ToLower(err.Error()), "cloudflare") {
				t.Fatal("invalid local relay configuration accepted")
			}
		})
	}
}

func TestLoad_WhenLocalCloudflareEnabled_PreservesPrivateSettingsAndDefaultOff(t *testing.T) {
	// Arrange
	validEnvironment(t)
	// Act
	disabled, err := Load()
	// Assert
	if err != nil || disabled.Imports().LocalCloudflareEnabled || disabled.Imports().LocalCloudflareKey != "" {
		t.Fatal("development relay enabled by default")
	}
	// Arrange
	key := strings.Repeat("a", 64)
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", "true")
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY", key)
	// Act
	enabled, err := Load()
	defaults := Config{ImportOptions: ImportOptions{LocalCloudflareEnabled: true, LocalCloudflareKey: key}}.Imports()
	// Assert
	if err != nil || !enabled.Imports().LocalCloudflareEnabled || enabled.Imports().LocalCloudflareKey != key || !defaults.LocalCloudflareEnabled || defaults.LocalCloudflareKey != key {
		t.Fatal("local-only relay configuration lost")
	}
}
