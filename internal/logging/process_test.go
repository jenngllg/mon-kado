package logging_test

import (
	"bytes"
	"encoding/json"
	"log/slog"
	"strings"
	"testing"

	"github.com/JennGllg/mon-kado/internal/logging"
	"github.com/google/uuid"
)

func TestProcessLogger_WhenLifecycleEventWritten_IncludesStableEventAndTraceIdentities(t *testing.T) {
	// Arrange
	var destination bytes.Buffer
	logger := logging.ProcessLogger(slog.New(slog.NewJSONHandler(&destination, nil)))
	// Act
	logger.InfoContext(t.Context(), "API listener started", "EventId", logging.APIListenerStarted)
	// Assert
	var event struct {
		CorrelationId, TraceId string
		EventId                int
	}
	if err := json.Unmarshal(destination.Bytes(), &event); err != nil {
		t.Fatal(err)
	}
	id, err := uuid.Parse(event.CorrelationId)
	if err != nil || id.Version() != 7 || event.TraceId != strings.ReplaceAll(event.CorrelationId, "-", "") || event.EventId != logging.APIListenerStarted {
		t.Fatal("process lifecycle scope is incomplete")
	}
}
