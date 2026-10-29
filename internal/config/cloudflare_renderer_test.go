package config

import (
	"strings"
	"testing"
)

func localRendererEnvironment(t *testing.T) {
	t.Helper()
	managedImportEnvironment(t)
	t.Setenv("APP_ENV", "local")
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ENABLED", "true")
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ORIGIN", "https://monkado-wish-render-local.example.workers.dev/")
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_KEY", strings.Repeat("c", 64))
}

func TestLoad_WhenLocalRendererRequested_RequiresExplicitLocalManagedConfiguration(t *testing.T) {
	for _, tc := range []struct{ key, value string }{
		{"WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ENABLED", "bad"},
		{"APP_ENV", "production"}, {"WISH_IMPORT_MANAGED_ENABLED", "false"},
		{"WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_KEY", "bad"},
		{"WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ORIGIN", "https://evil.example/"},
	} {
		t.Run(tc.key, func(t *testing.T) {
			// Arrange
			localRendererEnvironment(t)
			t.Setenv(tc.key, tc.value)
			// Act / Assert
			if _, err := Load(); err == nil {
				t.Fatal("unsafe renderer configuration accepted")
			}
		})
	}
	// Arrange
	localRendererEnvironment(t)
	// Act
	cfg, err := Load()
	// Assert
	options := cfg.Imports()
	if err != nil || !options.LocalCloudflareRenderEnabled || options.LocalCloudflareRenderOrigin != "https://monkado-wish-render-local.example.workers.dev" || options.LocalCloudflareRenderKey != strings.Repeat("c", 64) {
		t.Fatalf("explicit local configuration lost: %v", err)
	}
	t.Setenv("WISH_IMPORT_LOCAL_CLOUDFLARE_RENDER_ENABLED", "false")
	cfg, err = Load()
	if err != nil || cfg.Imports().LocalCloudflareRenderEnabled || cfg.Imports().LocalCloudflareRenderKey != "" {
		t.Fatal("renderer is not opt-in")
	}
}

func TestNormalizeCloudflareRenderOrigin_WhenAuthorityNotDedicated_Rejects(t *testing.T) {
	for _, value := range []string{"%", "http://monkado-wish-render-local.example.workers.dev", "https://monkado-wish-import.example.workers.dev", "https://monkado-wish-render-local.example.workers.dev.evil.fr", "https://u:p@monkado-wish-render-local.example.workers.dev", "https://monkado-wish-render-local.example.workers.dev:443", "https://monkado-wish-render-local.example.workers.dev/render", "https://monkado-wish-render-local.example.workers.dev?", "https://monkado-wish-render-local.example.workers.dev#secret"} {
		t.Run(value, func(t *testing.T) {
			// Arrange / Act
			_, err := NormalizeCloudflareRenderOrigin(value)
			// Assert
			if err == nil {
				t.Fatal("unsafe renderer origin accepted")
			}
		})
	}
}
