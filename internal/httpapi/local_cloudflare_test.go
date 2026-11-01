package httpapi_test

import (
	"io"
	"log/slog"
	"strings"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/importer"
)

func TestLocalCloudflareComposition_WhenEnvironmentAndToggleDiffer_PreservesProductionExclusion(t *testing.T) {
	for _, scenario := range []struct {
		name                           string
		production, enabled, installed bool
		fallback, fallbackInstalled    bool
	}{
		{"disabled local", false, false, false, false, false},
		{"enabled local", false, true, true, false, false},
		{"production manual override", true, true, false, true, false},
		{"enabled fallback", false, true, true, true, true},
		{"fallback without primary", false, false, false, true, false},
	} {
		t.Run(scenario.name, func(t *testing.T) {
			// Arrange
			cfg := config.Config{Production: scenario.production, ImportOptions: config.ImportOptions{LocalCloudflareEnabled: scenario.enabled, LocalCloudflareKey: strings.Repeat("a", 64), LocalFirecrawlEnabled: scenario.fallback, LocalFirecrawlKey: "fc-test", LocalCloudflareImagesEnabled: true, LocalFirecrawlProductEnabled: true, LocalFirecrawlImagesEnabled: true}}
			// Act
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			client := api.Importer.(*importer.Client)
			_, installed := client.PageSource.(*importer.LocalCloudflare)
			_, fallbackInstalled := client.FallbackSource.(*importer.LocalFirecrawl)
			_, imageInstalled := client.ImageSource.(*importer.LocalCloudflare)
			_, finalImageInstalled := client.FinalImageSource.(*importer.LocalFirecrawl)
			// Assert
			if installed != scenario.installed || imageInstalled != scenario.installed || finalImageInstalled != scenario.fallbackInstalled || fallbackInstalled != scenario.fallbackInstalled {
				t.Fatal("local Cloudflare environment boundary changed")
			}
		})
	}
}
