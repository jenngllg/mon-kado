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

func TestManagedImportComposition_WhenExplicitlyEnabled_InstallsOnlyRequestedProviders(t *testing.T) {
	for _, tc := range []struct {
		name                                           string
		production, managed, fallback, product, images bool
	}{
		{"production disabled", true, false, false, false, false},
		{"Cloudflare production", true, true, false, false, false},
		{"HTML fallback", true, true, true, false, false},
		{"product and images", true, true, true, true, true},
		{"managed local test", false, true, true, true, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			// Arrange
			cfg := config.Config{Production: tc.production, ImportOptions: config.ImportOptions{ManagedProvidersEnabled: tc.managed, CloudflareOrigin: "https://monkado-wish-import.account.workers.dev", CloudflareKey: "secret", FirecrawlEnabled: tc.fallback, FirecrawlProductEnabled: tc.product, FirecrawlImagesEnabled: tc.images, FirecrawlKey: "fc-test"}}
			cfg.ImportOptions.LocalCloudflareRenderEnabled = true
			cfg.ImportOptions.LocalCloudflareRenderOrigin = "https://monkado-wish-render-local.account.workers.dev"
			// Act
			api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
			client := api.Importer.(*importer.Client)
			// Assert
			_, primary := client.PageSource.(*importer.LocalCloudflare)
			_, fallback := client.FallbackSource.(*importer.LocalFirecrawl)
			_, images := client.FinalImageSource.(*importer.LocalFirecrawl)
			_, rendered := client.RenderedSource.(*importer.CloudflareRenderer)
			_, edgeImages := client.ImageSource.(*importer.LocalCloudflare)
			if rendered != (tc.managed && !tc.production) {
				t.Fatal("browser rendering escaped its local managed boundary")
			}
			if primary != tc.managed || fallback != tc.fallback || images != tc.images || edgeImages != rendered || client.Timeout != config.MaximumImportDuration {
				t.Fatal("managed provider composition changed")
			}
		})
	}
}
