package config

import "testing"

func TestLoad_WhenRollingFirecrawlExperimentIsConfigured_RequiresLocalAuthenticatedProvider(t *testing.T) {
	for _, tc := range []struct {
		name, environment, value string
		parent, valid, enabled   bool
	}{
		{"default off", "local", "false", true, true, false},
		{"local enabled", "local", "true", true, true, true},
		{"missing account", "local", "true", false, false, false},
		{"production denied", "production", "true", true, false, false},
		{"invalid setting", "local", "invalid", true, false, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			managedImportEnvironment(t)
			t.Setenv("APP_ENV", tc.environment)
			if !tc.parent {
				t.Setenv("WISH_IMPORT_FIRECRAWL_ENABLED", "false")
				t.Setenv("WISH_IMPORT_FIRECRAWL_PRODUCT_ENABLED", "false")
				t.Setenv("WISH_IMPORT_FIRECRAWL_IMAGES_ENABLED", "false")
			}
			t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_ROLLING_ENABLED", tc.value)

			// Act
			cfg, err := Load()

			// Assert
			if (err == nil) != tc.valid || err == nil && cfg.Imports().LocalFirecrawlRollingEnabled != tc.enabled {
				t.Fatalf("invalid rolling experiment configuration result: %v", err)
			}
		})
	}
}

func TestLoad_WhenKeylessFirecrawlExperimentIsConfigured_RequiresLocalAuthenticatedProvider(t *testing.T) {
	for _, tc := range []struct {
		name, environment, value string
		parent, valid, enabled   bool
	}{
		{"default off", "local", "false", true, true, false},
		{"local enabled", "local", "true", true, true, true},
		{"missing account", "local", "true", false, false, false},
		{"production denied", "production", "true", true, false, false},
		{"invalid setting", "local", "invalid", true, false, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			managedImportEnvironment(t)
			t.Setenv("APP_ENV", tc.environment)
			if !tc.parent {
				t.Setenv("WISH_IMPORT_FIRECRAWL_ENABLED", "false")
				t.Setenv("WISH_IMPORT_FIRECRAWL_PRODUCT_ENABLED", "false")
				t.Setenv("WISH_IMPORT_FIRECRAWL_IMAGES_ENABLED", "false")
			}
			t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_KEYLESS_ENABLED", tc.value)

			// Act
			cfg, err := Load()

			// Assert
			if (err == nil) != tc.valid || err == nil && cfg.Imports().LocalFirecrawlKeylessEnabled != tc.enabled {
				t.Fatalf("invalid experiment configuration result: %v", err)
			}
		})
	}
}

func TestLoad_WhenAccountImageExperimentIsConfigured_RequiresLocalKeylessImages(t *testing.T) {
	for _, tc := range []struct {
		name, environment, value string
		parent, valid            bool
	}{
		{"local enabled", "local", "true", true, true},
		{"missing image parent", "local", "true", false, false},
		{"production denied", "production", "true", false, false},
		{"invalid flag", "local", "invalid", true, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			managedImportEnvironment(t)
			t.Setenv("APP_ENV", tc.environment)
			if tc.parent {
				t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_KEYLESS_ENABLED", "true")
				t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_KEYLESS_IMAGES_ENABLED", "true")
			}
			t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_ACCOUNT_IMAGE_FALLBACK_ENABLED", tc.value)

			// Act
			cfg, err := Load()

			// Assert
			if (err == nil) != tc.valid || err == nil && !cfg.Imports().LocalFirecrawlAccountImageFallbackEnabled {
				t.Fatalf("invalid experiment configuration result: %v", err)
			}
		})
	}
}

func TestLoad_WhenKeylessImageExperimentIsConfigured_RequiresLocalKeylessFallback(t *testing.T) {
	for _, tc := range []struct {
		name, environment, value string
		parent, valid            bool
	}{
		{"local enabled", "local", "true", true, true},
		{"missing parent", "local", "true", false, false},
		{"production denied", "production", "true", false, false},
		{"invalid flag", "local", "invalid", true, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			managedImportEnvironment(t)
			t.Setenv("APP_ENV", tc.environment)
			if tc.parent {
				t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_KEYLESS_ENABLED", "true")
			}
			t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_KEYLESS_IMAGES_ENABLED", tc.value)

			// Act
			cfg, err := Load()

			// Assert
			if (err == nil) != tc.valid || err == nil && !cfg.Imports().LocalFirecrawlKeylessImagesEnabled {
				t.Fatalf("invalid experiment configuration result: %v", err)
			}
		})
	}
}
