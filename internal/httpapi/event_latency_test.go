package httpapi

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"testing"
	"testing/synctest"
	"time"

	"github.com/JennGllg/mon-kado/internal/application"
	"github.com/JennGllg/mon-kado/internal/config"
	"github.com/JennGllg/mon-kado/internal/logging"
)

func TestServeHTTP_WhenResponsesStream_RecordsFirstSuccessfulEventFlushAndPreservesOtherDurations(t *testing.T) {
	for _, tc := range []struct {
		name, contentType string
		startup           time.Duration
		flushError        bool
		abandoned         bool
		bucket            int
	}{
		{"healthy events", "text/event-stream", 10 * time.Millisecond, false, false, 0},
		{"slow event startup", "text/event-stream", 3 * time.Second, false, false, 6},
		{"failed event flush", "text/event-stream", 10 * time.Millisecond, true, false, 8},
		{"ordinary download", "application/octet-stream", 10 * time.Millisecond, false, false, 8},
		{"abandoned events", "text/event-stream", 10 * time.Millisecond, false, true, 0},
	} {
		t.Run(tc.name, func(t *testing.T) {
			synctest.Test(t, func(t *testing.T) {
				// Arrange: virtual time represents a long-lived response without wall-clock waits.
				cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
				var output bytes.Buffer
				ctx, cancel := context.WithCancel(t.Context())
				defer cancel()
				api := New(application.New(nil, cfg, time.Now), logging.NewJSONLogger(&output, "api", "local", "GET /test-events"))
				api.mux.HandleFunc("GET /test-events", func(w http.ResponseWriter, r *http.Request) {
					w.Header().Set("Content-Type", tc.contentType)
					time.Sleep(tc.startup)
					w.Write([]byte("event: ready\ndata: {}\n\n"))
					controller := http.NewResponseController(w)
					controller.Flush()
					time.Sleep(90 * time.Second)
					controller.Flush()
					if tc.abandoned {
						cancel()
					}
				})
				writer := &eventFlushWriter{ResponseRecorder: httptest.NewRecorder(), fail: tc.flushError}
				// Act
				api.ServeHTTP(writer, httptest.NewRequestWithContext(ctx, "GET", "http://example.test/test-events", nil))
				// Assert: a later heartbeat must not replace the first successful flush.
				measurements := api.Telemetry.Capture().HTTP
				abandoned := int64(0)
				if tc.abandoned {
					abandoned = 1
				}
				if measurements.Requests != 1 || measurements.DurationBuckets[tc.bucket] != 1 || measurements.ServerErrors != 0 || measurements.Abandoned != abandoned || writer.Code != 200 {
					t.Fatalf("unexpected response measurement: %+v", measurements)
				}
				var record struct {
					Properties struct{ DurationMilliseconds int64 }
				}
				if err := json.Unmarshal(output.Bytes(), &record); err != nil {
					t.Fatal(err)
				}
				if record.Properties.DurationMilliseconds != (tc.startup + 90*time.Second).Milliseconds() {
					t.Fatal("full stream lifetime was lost from the operational log")
				}
			})
		})
	}
}

type eventFlushWriter struct {
	*httptest.ResponseRecorder
	fail bool
}

func (w *eventFlushWriter) FlushError() error {
	if w.fail {
		return errors.New("test flush failure")
	}
	w.ResponseRecorder.Flush()
	return nil
}

func TestServeHTTP_WhenEventRequestIsRejected_RecordsRequestAndDuration(t *testing.T) {
	// Arrange
	cfg := config.Config{AllowedHosts: []string{"example.test"}, SigningKey: make([]byte, 32), EncryptionKey: make([]byte, 32)}
	api := New(application.New(nil, cfg, time.Now), slog.New(slog.NewJSONHandler(io.Discard, nil)))
	// Act
	writer := httptest.NewRecorder()
	api.ServeHTTP(writer, httptest.NewRequestWithContext(t.Context(), "GET", "http://example.test/api/v1/notifications/events", nil))
	// Assert
	measurements := api.Telemetry.Capture().HTTP
	var durations int64
	for _, count := range measurements.DurationBuckets {
		durations += count
	}
	if writer.Code != 401 || measurements.Requests != 1 || durations != 1 || measurements.ServerErrors != 0 || measurements.Abandoned != 0 {
		t.Fatal("rejected stream request was excluded from HTTP measurements")
	}
}
