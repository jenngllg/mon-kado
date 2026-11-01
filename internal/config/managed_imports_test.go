package config

import (
	"strings"
	"testing"
)

func managedImportEnvironment(t *testing.T) {
	t.Helper()
	validEnvironment(t)
	t.Setenv("APP_ENV", "production")
	t.Setenv("FRONTEND_ORIGIN", "https://www.monkado.fr")
	t.Setenv("PUBLIC_API_ORIGIN", "https://api.monkado.fr")
	t.Setenv("ALLOWED_ORIGINS", "https://www.monkado.fr")
	t.Setenv("ALLOWED_HOSTS", "api.monkado.fr")
	t.Setenv("WISH_IMPORT_MANAGED_ENABLED", "true")
	t.Setenv("WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://monkado-wish-import.example.workers.dev")
	t.Setenv("WISH_IMPORT_CLOUDFLARE_KEY", strings.Repeat("a", 64))
	t.Setenv("WISH_IMPORT_FIRECRAWL_ENABLED", "true")
	t.Setenv("WISH_IMPORT_FIRECRAWL_PRODUCT_ENABLED", "true")
	t.Setenv("WISH_IMPORT_FIRECRAWL_IMAGES_ENABLED", "true")
	t.Setenv("FIRECRAWL_API_KEY", "fc-"+strings.Repeat("b", 32))
}

func TestLoad_WhenManagedProvidersRequested_RequiresExplicitValidSettings(t *testing.T) {
	for _, tc := range []struct{ key, value string }{
		{"WISH_IMPORT_MANAGED_ENABLED", "invalid"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "http://monkado-wish-import.example.workers.dev"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://evil.example"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://monkado-wish-import.example.workers.dev.evil.example"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://user:password@monkado-wish-import.example.workers.dev"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://monkado-wish-import.example.workers.dev:444"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://monkado-wish-import.example.workers.dev/preview"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://monkado-wish-import.example.workers.dev?url=unsafe"},
		{"WISH_IMPORT_CLOUDFLARE_ORIGIN", "https://monkado-wish-import.example.workers.dev#secret"},
		{"WISH_IMPORT_CLOUDFLARE_KEY", "bad"},
		{"FIRECRAWL_API_KEY", ""},
		{"WISH_IMPORT_FIRECRAWL_ENABLED", "bad"},
		{"WISH_IMPORT_FIRECRAWL_PRODUCT_ENABLED", "bad"},
		{"WISH_IMPORT_FIRECRAWL_IMAGES_ENABLED", "bad"},
		{"WISH_IMPORT_MANAGED_ENABLED", "false"},
		{"WISH_IMPORT_FIRECRAWL_ENABLED", "false"},
	} {
		t.Run(tc.key+tc.value, func(t *testing.T) {
			// Arrange
			managedImportEnvironment(t)
			t.Setenv(tc.key, tc.value)
			// Act / Assert
			if _, err := Load(); err == nil {
				t.Fatal("invalid managed provider configuration accepted")
			}
		})
	}
}

func TestLoad_WhenManagedProductionConfigured_PreservesShortBudgetAndSecrets(t *testing.T) {
	// Arrange
	managedImportEnvironment(t)
	// Act
	result, err := Load()
	options := result.Imports()
	// Assert
	if err != nil || !result.Production || !options.ManagedProvidersEnabled || options.Timeout != MaximumImportDuration || !options.FirecrawlEnabled || !options.FirecrawlProductEnabled || !options.FirecrawlImagesEnabled || options.CloudflareKey != strings.Repeat("a", 64) || options.FirecrawlKey != "fc-"+strings.Repeat("b", 32) {
		t.Fatalf("managed provider settings changed: %v", err)
	}
}

func TestNormalizeCloudflareRelayOrigin_WhenTrailingSlashUsed_ReturnsFixedOrigin(t *testing.T) {
	// Arrange / Act
	actual, err := NormalizeCloudflareRelayOrigin("https://monkado-wish-import.example.workers.dev/")
	// Assert
	if err != nil || actual != "https://monkado-wish-import.example.workers.dev" {
		t.Fatal("origin normalization changed")
	}
}
