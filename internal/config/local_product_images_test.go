package config

import (
	"strings"
	"testing"
)

func TestLoad_WhenProductOrImageExperimentRequested_RequiresLocalParentAndExplicitToggle(t *testing.T) {
	for _, key := range []string{"WISH_IMPORT_LOCAL_FIRECRAWL_PRODUCT_ENABLED", "WISH_IMPORT_LOCAL_CLOUDFLARE_IMAGES_ENABLED", "WISH_IMPORT_LOCAL_FIRECRAWL_IMAGES_ENABLED"} {
		for _, scenario := range []struct {
			name, value, environment, cloudflare, firecrawl string
			valid                                           bool
		}{
			{"default off", "false", "local", "false", "false", true},
			{"bad flag", "bad", "local", "true", "true", false},
			{"no parent", "true", "local", "false", "false", false},
			{"production", "true", "production", "true", "true", false},
			{"enabled local", "true", "local", "true", "true", true},
		} {
			t.Run(key+"/"+scenario.name, func(t *testing.T) {
				// Arrange
				validEnvironment(t)
				t.Setenv("APP_ENV", scenario.environment)
				t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_ENABLED", scenario.cloudflare)
				t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_KEY", strings.Repeat("a", 64))
				t.Setenv("WISH_IMPORT_LOCAL_FIRECRAWL_ENABLED", scenario.firecrawl)
				t.Setenv("FIRECRAWL_API_KEY", "fc-"+strings.Repeat("a", 32))
				t.Setenv(key, scenario.value)
				// Act
				cfg, err := Load()
				// Assert
				if (err == nil) != scenario.valid {
					t.Fatalf("configuration error %v", err)
				}
				if err == nil {
					enabled := cfg.Imports().LocalCloudflareImagesEnabled
					if strings.Contains(key, "PRODUCT") {
						enabled = cfg.Imports().LocalFirecrawlProductEnabled
					} else if strings.Contains(key, "FIRECRAWL") {
						enabled = cfg.Imports().LocalFirecrawlImagesEnabled
					}
					if enabled != (scenario.value == "true") {
						t.Fatal("explicit toggle lost")
					}
				}
			})
		}
	}
}
