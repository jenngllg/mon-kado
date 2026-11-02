package httpapi_test

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/httpapi"
	"github.com/JennGllg/mon-kado/internal/logging"
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

func TestServeHTTP_WhenRequestCompletes_LogsDurationAndCompiledOperationWithoutURL(t *testing.T) {
	// Arrange
	var output bytes.Buffer
	cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
	api := httpapi.New(application.New(nil, cfg, time.Now), logging.NewJSONLogger(&output, "api", "local", "GET /liveness"))
	r := httptest.NewRequestWithContext(t.Context(), "GET", "http://example.test/liveness?token=private-token", nil)
	w := httptest.NewRecorder()
	// Act
	api.ServeHTTP(w, r)
	// Assert
	var record struct {
		EventName  string         `json:"eventName"`
		Properties map[string]any `json:"properties"`
	}
	decoder := json.NewDecoder(&output)
	completed := 0
	for {
		if err := decoder.Decode(&record); err == io.EOF {
			break
		} else if err != nil {
			t.Fatal(err)
		}
		if record.EventName == "HTTPRequestCompleted" {
			completed++
			break
		}
	}
	duration, valid := record.Properties["DurationMilliseconds"].(float64)
	if w.Code != 200 || completed != 1 || !valid || duration < 0 || record.Properties["Operation"] != "GET /liveness" || record.Properties["Abandoned"] != false || bytes.Contains(output.Bytes(), []byte("private-token")) {
		t.Fatal("HTTP duration or safe operation classification missing")
	}
}
