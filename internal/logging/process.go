package logging

import (
	"log/slog"
	"strings"

	"github.com/JennGllg/mon-kado/internal/security"
)

// ProcessLogger attaches correlation and trace identities to native lifecycle events.
func ProcessLogger(logger *slog.Logger) *slog.Logger {
	id := security.NewID()
	return logger.With("CorrelationId", id, "TraceId", strings.ReplaceAll(id, "-", ""))
}
