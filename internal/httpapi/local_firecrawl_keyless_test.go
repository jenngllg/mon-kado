package httpapi_test

import (
	"io"
	"log/slog"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestKeylessComposition_WhenEnvironmentAndToggleDiffer_OnlyInstallsLocally(t *testing.T) {
	for _, tc := range []struct {
		name                         string
		production, enabled, present bool
	}{
		{"disabled", false, false, false},
		{"local enabled", false, true, true},
		{"production manual override", true, true, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			cfg := config.Config{Production: tc.production, ImportOptions: config.ImportOptions{ManagedProvidersEnabled: true, FirecrawlEnabled: true, FirecrawlKey: "fc-test", LocalFirecrawlKeylessEnabled: tc.enabled}}

			// Act
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			client := api.Importer.(*importer.Client)
			_, present := client.FallbackSource.(*importer.LocalFirecrawlKeylessFirst)

			// Assert
			if present != tc.present {
				t.Fatal("keyless experiment escaped its local environment")
			}
		})
	}
}

func TestKeylessImageComposition_WhenEnabled_UsesFreeProviderOnlyInLocalEnvironment(t *testing.T) {
	for _, tc := range []struct {
		name                         string
		production, enabled, present bool
	}{
		{"disabled", false, false, false},
		{"local enabled", false, true, true},
		{"production manual override", true, true, false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			cfg := config.Config{Production: tc.production, ImportOptions: config.ImportOptions{ManagedProvidersEnabled: true, FirecrawlEnabled: true, FirecrawlKey: "fc-test", LocalFirecrawlKeylessEnabled: true, LocalFirecrawlKeylessImagesEnabled: tc.enabled}}

			// Act
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			client := api.Importer.(*importer.Client)
			_, present := client.FinalImageSource.(*importer.LocalFirecrawlKeylessFirst)

			// Assert
			if present != tc.present {
				t.Fatal("keyless image recovery escaped its local environment")
			}
		})
	}
}
