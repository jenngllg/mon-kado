package httpapi_test

import (
	"context"
	"io"
	"log/slog"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
)

func TestServeHTTP_WhenRequestsCompleteOrAreAbandoned_RecordsActualBoundaryOutcomes(t *testing.T) {
	// Arrange
	cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32), PerimeterOptions: config.PerimeterOptions{PermitLimit: 1, Window: time.Minute}}
	api := httpapi.New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
	request := func(path string, ctx context.Context) int {
		r := httptest.NewRequestWithContext(ctx, "GET", "http://example.test"+path, nil)
		w := httptest.NewRecorder()
		api.ServeHTTP(w, r)
		return w.Code
	}
	// Act
	if request("/liveness", t.Context()) != 200 || request("/api/v1/wishlists", t.Context()) != 401 || request("/api/v1/wishlists", t.Context()) != 429 {
		t.Fatal("request scenario did not exercise expected outcomes")
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	request("/liveness", ctx)
	// Assert
	measurements := api.Telemetry.Capture().HTTP
	var buckets int64
	for _, count := range measurements.DurationBuckets {
		buckets += count
	}
	if measurements.Requests != 4 || measurements.Throttled != 1 || measurements.Abandoned != 1 || measurements.ServerErrors != 0 || buckets != 4 {
		t.Fatal("HTTP boundary measurement differs from actual requests")
	}
}
