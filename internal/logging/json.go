package logging

import (
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"regexp"
	"strings"
	"sync"

	"github.com/JennGllg/mon-kado/internal/domain"
	"github.com/JennGllg/mon-kado/internal/security"
	"github.com/google/uuid"
)

var events = map[int64]string{
	BusinessOperationSucceeded: "BusinessOperationSucceeded", HTTPRequestCompleted: "HTTPRequestCompleted",
	BusinessOperationFailed: "BusinessOperationFailed", UnhandledRequestPanic: "UnhandledRequestPanic",
	UnexpectedDependencyFailure: "UnexpectedDependencyFailure", UnhandledRequestFailure: "UnhandledRequestFailure",
	APIListenerStarted: "APIListenerStarted", APIStopped: "APIStopped", WorkerStopped: "WorkerStopped",
	MigrationFailed: "MigrationFailed", MigrationsApplied: "MigrationsApplied", GoogleCallbackRejected: "GoogleCallbackRejected",
	WorkerPassFailed: "WorkerPassFailed", AuthenticationEmailDelivered: "AuthenticationEmailDelivered",
	AuthenticationEmailAttemptFailed: "AuthenticationEmailAttemptFailed", WorkerPassCompleted: "WorkerPassCompleted",
	ArchiveDownloadInterrupted: "ArchiveDownloadInterrupted", NotificationAttemptFailed: "NotificationAttemptFailed",
	TelemetrySnapshotFailed: "TelemetrySnapshotFailed",
}

var hexadecimalTrace = regexp.MustCompile(`^[a-fA-F0-9]{32}$`)
var publishedRevision = regexp.MustCompile(`^[a-fA-F0-9]{40}$`)
var technicalType = regexp.MustCompile(`^\*?[A-Za-z][A-Za-z0-9_.]*$`)
var technicalFrame = regexp.MustCompile(`^[A-Za-z0-9_./*()+-]+$`)

type jsonOutput struct {
	mu     sync.Mutex
	writer io.Writer
}

type jsonHandler struct {
	output                               *jsonOutput
	service, version, correlation, trace string
	operations                           map[string]bool
	attributes                           []slog.Attr
	grouped                              bool
}

// NewJSONLogger writes the compiled event vocabulary and allowlisted technical properties only.
// Messages, error contents, arbitrary groups and unregistered resource paths are never rendered.
func NewJSONLogger(output io.Writer, service, version string, operations ...string) *slog.Logger {
	if service != "api" && service != "worker" && service != "migrate" {
		service = "external"
	}
	if version != "local" && !publishedRevision.MatchString(version) {
		version = "local"
	}
	id := security.NewID()
	h := &jsonHandler{output: &jsonOutput{writer: output}, service: service, version: version, correlation: id, trace: strings.ReplaceAll(id, "-", ""), operations: map[string]bool{}}
	for _, operation := range operations {
		h.operations[operation] = true
	}
	return slog.New(h)
}

func (h *jsonHandler) Enabled(_ context.Context, level slog.Level) bool {
	return level >= slog.LevelInfo
}

func (h *jsonHandler) WithAttrs(attributes []slog.Attr) slog.Handler {
	copy := *h
	if !h.grouped {
		copy.attributes = append(append([]slog.Attr{}, h.attributes...), attributes...)
	}
	return &copy
}

func (h *jsonHandler) WithGroup(name string) slog.Handler {
	copy := *h
	copy.grouped = h.grouped || name != ""
	return &copy
}

func (h *jsonHandler) Handle(_ context.Context, record slog.Record) error {
	attributes := append([]slog.Attr{}, h.attributes...)
	if !h.grouped {
		record.Attrs(func(attribute slog.Attr) bool { attributes = append(attributes, attribute); return true })
	}
	var eventID int64
	for _, attribute := range attributes {
		if attribute.Key == "EventId" && attribute.Value.Kind() == slog.KindInt64 {
			eventID = attribute.Value.Int64()
		}
	}
	event, known := events[eventID]
	if !known {
		event, eventID = "ExternalEvent", 0
	}
	level := "Information"
	if record.Level >= slog.LevelError {
		level = "Error"
	} else if record.Level >= slog.LevelWarn {
		level = "Warning"
	}
	result := map[string]any{"timestamp": record.Time.UTC(), "service": h.service, "version": h.version, "level": level, "category": "MonKado." + h.service, "eventId": eventID, "eventName": event, "correlationId": h.correlation, "traceId": h.trace}
	properties := map[string]any{}
	for _, attribute := range attributes {
		value := attribute.Value
		if value.Kind() == slog.KindString {
			text := value.String()
			switch attribute.Key {
			case "CorrelationId":
				if id, err := uuid.Parse(text); err == nil && id != uuid.Nil {
					result["correlationId"] = text
				}
			case "TraceId":
				if hexadecimalTrace.MatchString(text) && strings.Trim(text, "0") != "" {
					result["traceId"] = text
				}
			case "ExceptionType":
				if known && exceptionalEvent(eventID) && len(text) <= 100 && technicalType.MatchString(text) {
					result["exceptionType"] = text
				}
			case "StackTrace":
				if known && exceptionalEvent(eventID) {
					result["stackTrace"] = safeFrames(text)
				}
			}
		}
		if known {
			if safe := h.property(attribute); safe != nil {
				properties[attribute.Key] = safe
			}
		}
	}
	result["properties"] = properties
	h.output.mu.Lock()
	defer h.output.mu.Unlock()
	return json.NewEncoder(h.output.writer).Encode(result)
}

func exceptionalEvent(id int64) bool {
	return id == UnhandledRequestPanic || id == UnhandledRequestFailure || id == UnexpectedDependencyFailure || id == WorkerPassFailed || id == APIStopped || id == WorkerStopped || id == MigrationFailed
}

func safeFrames(stack string) string {
	frames := []string{}
	for _, line := range strings.Split(stack, "\n") {
		if len(frames) == 64 {
			break
		}
		if line == "" || line[0] == '\t' {
			continue
		}
		index := strings.LastIndex(line, "(")
		if index < 0 {
			continue
		}
		name := line[:index]
		if len(name) <= 250 && technicalFrame.MatchString(name) {
			frames = append(frames, name+"()")
		}
	}
	return strings.Join(frames, "\n")
}

func (h *jsonHandler) property(attribute slog.Attr) any {
	value := attribute.Value
	switch attribute.Key {
	case "Count", "DeletedAccountCount", "DeletedEmailCount", "DeletedRequestCount", "DeletedSessionCount", "TotalCount", "StatusCode", "AttemptCount":
		if value.Kind() == slog.KindInt64 && value.Int64() >= 0 {
			return value.Int64()
		}
	case "Abandoned", "IsReferenced":
		if value.Kind() == slog.KindBool {
			return value.Bool()
		}
	}
	if value.Kind() != slog.KindString {
		return nil
	}
	text := value.String()
	switch attribute.Key {
	case "AdministratorId", "ExportId", "ImageId", "MemberId", "MessageId", "OperationId", "OutboxId", "OutboxMessageId", "OwnerId", "ParticipantId", "ReportId", "RequestId", "ReservationId", "ShareLinkId", "UserId", "WishId", "WishlistId":
		if id, err := uuid.Parse(text); err == nil && id != uuid.Nil {
			return id.String()
		}
	case "ErrorCode":
		if domain.KnownErrorCode(text) {
			return text
		}
	case "Method":
		switch text {
		case "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "Other":
			return text
		}
	case "Operation":
		if h.operations[text] {
			return text
		}
	case "Job":
		switch text {
		case "exports", "authenticationEmail", "moderationEmail", "erasureEmail", "erasureExpiry", "images", "authenticationCleanup":
			return text
		}
	case "Category", "FailureCategory":
		switch text {
		case "processing", "network", "throttled", "authorization", "rejected", "unavailable", "circuitOpen", "storage":
			return text
		}
	}
	return nil
}
